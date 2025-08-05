using System;

namespace EldenRingTool.Util
{
    public static class CodeCaveOffsets
    {
        public static IntPtr Base;

        public enum ReducedTargetView
        {
            MaxDist = 0x0,
            Code = 0x10
        }

        public const int Rykard = 0x200;
        public const int InfinitePoise = 0x300;
    }
}