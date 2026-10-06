using System;

namespace JonsboCanvas
{
    internal static class FrameRotation
    {
        internal static void GetTargetSize(
            int sourceWidth, int sourceHeight, int rotationDegrees,
            out int targetWidth, out int targetHeight)
        {
            if (rotationDegrees == 90 || rotationDegrees == 270)
            {
                targetWidth = sourceHeight;
                targetHeight = sourceWidth;
                return;
            }
            if (rotationDegrees == 0 || rotationDegrees == 180)
            {
                targetWidth = sourceWidth;
                targetHeight = sourceHeight;
                return;
            }
            throw new ArgumentOutOfRangeException("rotationDegrees");
        }

        internal static void MapPixel(
            int sourceX, int sourceY, int sourceWidth, int sourceHeight, int rotationDegrees,
            out int targetX, out int targetY)
        {
            if (rotationDegrees == 90)
            {
                targetX = sourceHeight - 1 - sourceY;
                targetY = sourceX;
                return;
            }
            if (rotationDegrees == 180)
            {
                targetX = sourceWidth - 1 - sourceX;
                targetY = sourceHeight - 1 - sourceY;
                return;
            }
            if (rotationDegrees == 270)
            {
                targetX = sourceY;
                targetY = sourceWidth - 1 - sourceX;
                return;
            }
            if (rotationDegrees == 0)
            {
                targetX = sourceX;
                targetY = sourceY;
                return;
            }
            throw new ArgumentOutOfRangeException("rotationDegrees");
        }
    }
}
