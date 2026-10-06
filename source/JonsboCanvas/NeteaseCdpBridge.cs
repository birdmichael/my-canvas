using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace JonsboCanvas
{
    internal sealed class NeteasePlaybackSnapshot
    {
        public bool Connected;
        public bool HasPosition;
        public bool Playing;
        public string SongId = "";
        public double PositionSeconds;
        public double DurationSeconds;
    }

    internal sealed class NeteaseCdpBridge : IDisposable
    {
        private sealed class CdpTarget
        {
            public string type { get; set; }
            public string url { get; set; }
            public string webSocketDebuggerUrl { get; set; }
        }

        private sealed class CdpEnvelope
        {
            public int id { get; set; }
            public string method { get; set; }
            public CdpParameters @params { get; set; }
            public CdpCommandResult result { get; set; }
            public CdpError error { get; set; }
        }

        private sealed class CdpCommandResult
        {
            public CdpRemoteObject result { get; set; }
        }

        private sealed class CdpRemoteObject
        {
            public object value { get; set; }
        }

        private sealed class CdpError
        {
            public string message { get; set; }
        }

        private sealed class CdpParameters
        {
            public string name { get; set; }
            public string payload { get; set; }
        }

        private sealed class BridgePayload
        {
            public string eventName { get; set; }
            public object[] args { get; set; }
        }

        private string _bindingName = "";
        private readonly object _sync = new object();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly Thread _worker;
        private readonly int _port;
        private ClientWebSocket _socket;
        private int _commandId;
        private int _installCommandId;
        private bool _connected;
        private bool _playing;
        private string _songId = "";
        private double _positionSeconds;
        private double _durationSeconds;
        private DateTime _positionUtc;
        private DateTime _lastProgressLogUtc;
        private bool _disposed;

        public bool Connected
        {
            get { lock (_sync) return _connected; }
        }

        public void Reconnect()
        {
            try
            {
                ClientWebSocket socket = _socket;
                if (socket != null)
                    socket.Abort();
            }
            catch
            {
            }
        }

        public NeteaseCdpBridge(int port)
        {
            _port = port;
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Name = "Jonsbo-Netease-CDP";
            _worker.Start();
        }

        public NeteasePlaybackSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                double position = _positionSeconds;
                double age = _positionUtc == DateTime.MinValue ? 0 :
                    Math.Max(0, (DateTime.UtcNow - _positionUtc).TotalSeconds);
                bool streamFresh = _positionUtc != DateTime.MinValue &&
                    (!_playing || age <= 6.0);
                bool effectivePlaying = _playing && streamFresh;
                if (effectivePlaying)
                    position += age;
                if (_durationSeconds > 0)
                    position = Math.Min(position, _durationSeconds);
                return new NeteasePlaybackSnapshot
                {
                    Connected = _connected,
                    HasPosition = streamFresh,
                    Playing = effectivePlaying,
                    SongId = _songId,
                    PositionSeconds = Math.Max(0, position),
                    DurationSeconds = Math.Max(0, _durationSeconds)
                };
            }
        }

        private void WorkerLoop()
        {
            while (!_cancel.IsCancellationRequested)
            {
                try
                {
                    string endpoint = FindDebuggerEndpoint();
                    if (string.IsNullOrWhiteSpace(endpoint))
                    {
                        Wait(2000);
                        continue;
                    }

                    using (ClientWebSocket socket = new ClientWebSocket())
                    {
                        _socket = socket;
                        // A CDP Runtime binding belongs to its WebSocket session.
                        // Generate a new name for every connection, including
                        // automatic reconnects, so a surviving page function can
                        // never point at a closed session.
                        _bindingName = "jonsboPlayback_" + Guid.NewGuid().ToString("N");
                        // CEF's DevTools endpoint closes the connection when the .NET
                        // WebSocket keep-alive frame is sent.  The old 15 second value
                        // therefore caused a reconnect loop and temporarily dropped back
                        // to cache-based progress estimation.  CDP traffic is already
                        // frequent while music is playing, so disable protocol keep-alive.
                        socket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
                        socket.ConnectAsync(new Uri(endpoint), _cancel.Token).Wait();
                        Log.Write("Netease realtime socket connected; validating native callbacks");

                        SendCommand("Runtime.enable", null);
                        SendCommand("Runtime.addBinding", new Dictionary<string, object>
                        {
                            { "name", _bindingName }
                        });
                        _installCommandId = InstallCallbacks();
                        ReceiveLoop(socket);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (!_cancel.IsCancellationRequested)
                        Log.Write("Netease realtime bridge retry: " + exception.Message);
                }
                finally
                {
                    _socket = null;
                    lock (_sync)
                    {
                        // A disconnected native event stream has no authoritative
                        // now-playing state. Clear it instead of extrapolating or
                        // combining it with a cache-derived song id.
                        _connected = false;
                        _playing = false;
                        _songId = "";
                        _positionSeconds = 0;
                        _durationSeconds = 0;
                        _positionUtc = DateTime.MinValue;
                        _lastProgressLogUtc = DateTime.MinValue;
                        _bindingName = "";
                    }
                }
                Wait(1500);
            }
        }

        private string FindDebuggerEndpoint()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    "http://127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + "/json/list");
                request.Timeout = 800;
                request.ReadWriteTimeout = 800;
                request.Proxy = null;
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    List<CdpTarget> targets = _serializer.Deserialize<List<CdpTarget>>(reader.ReadToEnd());
                    if (targets == null)
                        return "";
                    // Desktop lyrics and other auxiliary windows are also CDP
                    // pages. Only the main app owns the authoritative player SDK.
                    foreach (CdpTarget target in targets)
                    {
                        if (target != null && target.type == "page" &&
                            string.Equals(target.url, "orpheus://orpheus/pub/app.html", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(target.webSocketDebuggerUrl))
                            return target.webSocketDebuggerUrl;
                    }
                    foreach (CdpTarget target in targets)
                    {
                        if (target != null && target.type == "page" &&
                            !string.IsNullOrWhiteSpace(target.webSocketDebuggerUrl))
                            return target.webSocketDebuggerUrl;
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        private void ReceiveLoop(ClientWebSocket socket)
        {
            byte[] buffer = new byte[32768];
            using (MemoryStream message = new MemoryStream())
            {
                while (!_cancel.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult received = socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), _cancel.Token).Result;
                    if (received.MessageType == WebSocketMessageType.Close)
                        break;
                    message.Write(buffer, 0, received.Count);
                    if (!received.EndOfMessage)
                        continue;

                    string json = Encoding.UTF8.GetString(message.ToArray());
                    message.SetLength(0);
                    HandleCdpMessage(json);
                }
            }
        }

        private void HandleCdpMessage(string json)
        {
            try
            {
                CdpEnvelope envelope = _serializer.Deserialize<CdpEnvelope>(json);
                if (envelope == null)
                    return;
                if (envelope.id != 0 && envelope.id == _installCommandId)
                {
                    string status = envelope.result == null || envelope.result.result == null
                        ? "" : Convert.ToString(envelope.result.result.value,
                            CultureInfo.InvariantCulture) ?? "";
                    bool installed = status.StartsWith("jonsbo-installed-v8-", StringComparison.Ordinal);
                    lock (_sync) _connected = installed;
                    if (installed)
                        Log.Write("Netease realtime callbacks installed: " + status);
                    else
                        Log.Write("Netease realtime callback install failed: " +
                            (envelope.error == null ? OrValue(status, "unknown response") :
                                OrValue(envelope.error.message, "CDP error")));
                }
                else if (envelope.method == "Runtime.bindingCalled" && envelope.@params != null &&
                    envelope.@params.name == _bindingName)
                {
                    HandlePlaybackPayload(envelope.@params.payload);
                }
                else if (envelope.method == "Runtime.executionContextCreated")
                {
                    _installCommandId = InstallCallbacks();
                }
            }
            catch (Exception exception)
            {
                Log.Write("Netease bridge message ignored: " + exception.Message);
            }
        }

        private void HandlePlaybackPayload(string json)
        {
            BridgePayload payload = _serializer.Deserialize<BridgePayload>(json);
            object[] args = payload == null ? null : payload.args;
            if (payload == null || args == null)
                return;

            bool logProgress = false;
            string logSongId = "";
            double logPosition = 0;
            lock (_sync)
            {
                // A native event is the strongest possible health signal. Keep
                // the public connection state honest even if a CEF build omits
                // the Runtime.evaluate return value.
                _connected = true;
                // Protocol mirrored from the open-source BetterNCM projects
                // Taskbar-Lyrics and LibSongInfo:
                // PlayProgress(audioId, seconds, loadProgress).
                if (payload.eventName == "progress" && args.Length >= 2)
                {
                    SetSongId(args[0]);
                    _positionSeconds = Math.Max(0, ToDouble(args[1]));
                    _positionUtc = DateTime.UtcNow;
                    _playing = true;
                    if ((_positionUtc - _lastProgressLogUtc).TotalSeconds >= 10.0)
                    {
                        _lastProgressLogUtc = _positionUtc;
                        logProgress = true;
                        logSongId = _songId;
                        logPosition = _positionSeconds;
                    }
                }
                else if (payload.eventName == "load" && args.Length >= 1)
                {
                    SetSongId(args[0]);
                    if (args.Length >= 2)
                    {
                        Dictionary<string, object> info = args[1] as Dictionary<string, object>;
                        object duration;
                        if (info != null && info.TryGetValue("duration", out duration))
                            _durationSeconds = NormalizeDuration(ToDouble(duration));
                    }
                    _positionSeconds = 0;
                    _positionUtc = DateTime.UtcNow;
                }
                else if (payload.eventName == "state" && args.Length >= 2)
                {
                    SetSongId(args[0]);
                    double position = GetSnapshotPositionUnsafe();
                    // NetEase 3.x sends (playId, resumeOrPauseId, state).
                    // The second argument is an operation id, not the state.
                    string state = Convert.ToString(args.Length >= 3 ? args[2] : args[1], CultureInfo.InvariantCulture) ?? "";
                    bool nextPlaying = _playing;
                    if (state == "2" || state.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        state.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0)
                        nextPlaying = false;
                    else if (state == "1" || state.IndexOf("resume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(state, "play", StringComparison.OrdinalIgnoreCase) ||
                        state.IndexOf("|play|", StringComparison.OrdinalIgnoreCase) >= 0)
                        nextPlaying = true;
                    _positionSeconds = position;
                    _positionUtc = DateTime.UtcNow;
                    _playing = nextPlaying;
                }
                else if (payload.eventName == "end" && args.Length >= 1)
                {
                    SetSongId(args[0]);
                    _positionSeconds = _durationSeconds > 0
                        ? _durationSeconds : GetSnapshotPositionUnsafe();
                    _positionUtc = DateTime.UtcNow;
                    _playing = false;
                }
            }
            if (logProgress)
                Log.Write(string.Format(CultureInfo.InvariantCulture,
                    "Netease native progress active: {0} @ {1:F2}s", logSongId, logPosition));
        }

        private double GetSnapshotPositionUnsafe()
        {
            if (!_playing || _positionUtc == DateTime.MinValue)
                return _positionSeconds;
            return _positionSeconds + Math.Max(0, (DateTime.UtcNow - _positionUtc).TotalSeconds);
        }

        private void SetSongId(object value)
        {
            string raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            int separator = raw.IndexOf('_');
            string next = separator > 0 ? raw.Substring(0, separator) : raw;
            if (next.Length > 0 && next != _songId)
            {
                _songId = next;
                _positionSeconds = 0;
                _durationSeconds = 0;
                _positionUtc = DateTime.UtcNow;
            }
        }

        private int InstallCallbacks()
        {
            string bindingLiteral = _serializer.Serialize(_bindingName);
            string script = @"(()=>{
if(typeof channel!=='object'||typeof channel.registerCall!=='function')return 'channel-unavailable';
const bindingName=" + bindingLiteral + @";
window.__jonsboPlaybackBindingName=bindingName;
window.__jonsboPlaybackDispatch=(eventName,args)=>{
  try{window[window.__jonsboPlaybackBindingName](JSON.stringify({eventName:eventName,args:Array.from(args)}));}catch(e){}
};
const createForwarder=(eventName)=>(...args)=>{
  const dispatch=window.__jonsboPlaybackDispatch;
  if(typeof dispatch==='function')dispatch(eventName,args);
};
if(window.__jonsboPlaybackProtocolVersion!==8){
  try{
    // Modern NetEase multiplexes native listeners through its shared SDK.
    // Subscribe to its existing observables instead of replacing a native
    // callback used by the player. Resolve the loaded module by capability;
    // module numbers change between client releases.
    let sdk=null;
    if(Array.isArray(window.webpackJsonp)){
      let req=window.__myCanvasWebpackRequire;
      if(!req){
        const id='mycanvas_bridge_'+Date.now();
        window.webpackJsonp.push([[id],{[id]:(m,e,r)=>{window.__myCanvasWebpackRequire=r;}},[[id]]]);
        req=window.__myCanvasWebpackRequire;
      }
      if(req&&req.c){
        for(const module of Object.values(req.c)){
          const exports=module&&module.exports;
          if(!exports)continue;
          const candidates=[exports];
          if(typeof exports==='object'){
            for(const key of Object.keys(exports)){
              try{candidates.push(exports[key]);}catch(e){}
            }
          }
          sdk=candidates.find(x=>x&&typeof x.rxFromNativeEvent==='function'&&typeof x.call==='function');
          if(sdk)break;
        }
      }
    }
    const events=[['audioplayer.onLoad','load'],['audioplayer.onPlayProgress','progress'],['audioplayer.onPlayState','state'],['audioplayer.onEnd','end']];
    if(sdk){
      const subscriptions=[];
      try{
        for(const [nativeEvent,eventName] of events){
          subscriptions.push(sdk.rxFromNativeEvent(nativeEvent).subscribe(args=>{
            const dispatch=window.__jonsboPlaybackDispatch;
            if(typeof dispatch==='function')dispatch(eventName,args);
          }));
        }
      }catch(e){
        for(const subscription of subscriptions)subscription.unsubscribe();
        throw e;
      }
      window.__myCanvasPlaybackSubscriptions=subscriptions;
      window.__myCanvasPlaybackTransport='sdk';
    }else{
      // Legacy clients have no shared SDK and use the original native hooks.
      // A hacked registerCall without an SDK is unsafe: it may replace a
      // listener owned by the player, so report incompatibility explicitly.
      if(channel.registerCall.__HACKED__)return 'sdk-unavailable';
      for(const [nativeEvent,eventName] of events)channel.registerCall(nativeEvent,createForwarder(eventName));
      window.__myCanvasPlaybackTransport='legacy';
    }
    window.__jonsboPlaybackProtocolVersion=8;
  }catch(e){
    return 'register-failed:'+(e&&e.message?e.message:String(e));
  }
}
return window.__jonsboPlaybackProtocolVersion===8?'jonsbo-installed-v8-'+window.__myCanvasPlaybackTransport:'register-failed';
})()";
            return SendCommand("Runtime.evaluate", new Dictionary<string, object>
            {
                { "expression", script },
                { "returnByValue", true }
            });
        }

        private int SendCommand(string method, object parameters)
        {
            ClientWebSocket socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
                return 0;
            Dictionary<string, object> command = new Dictionary<string, object>();
            int commandId = Interlocked.Increment(ref _commandId);
            command["id"] = commandId;
            command["method"] = method;
            if (parameters != null)
                command["params"] = parameters;
            byte[] bytes = Encoding.UTF8.GetBytes(_serializer.Serialize(command));
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                _cancel.Token).Wait();
            return commandId;
        }

        private static string OrValue(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static double ToDouble(object value)
        {
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static double NormalizeDuration(double value)
        {
            // Some NetEase builds expose seconds while others expose milliseconds.
            return value > 10000 ? value / 1000.0 : Math.Max(0, value);
        }

        private void Wait(int milliseconds)
        {
            _cancel.Token.WaitHandle.WaitOne(milliseconds);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cancel.Cancel();
            try
            {
                ClientWebSocket socket = _socket;
                if (socket != null)
                    socket.Abort();
            }
            catch { }
            if (_worker != null && _worker.IsAlive)
                _worker.Join(1500);
            _cancel.Dispose();
        }
    }
}
