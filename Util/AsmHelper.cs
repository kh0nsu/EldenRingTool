using System;

namespace EldenRingTool.Util
{
    public static class AsmHelper
    {
        public static int GetRelOffset(IntPtr srcInstrAddr, IntPtr targetAddr, int instrLength = 0)
            => (int)(targetAddr.ToInt64() - (srcInstrAddr.ToInt64() + instrLength));

        public static byte[] GetRelOffsetBytes(IntPtr srcInstrAddr, IntPtr targetAddr, int instrLength = 0)
            => BitConverter.GetBytes(GetRelOffset(srcInstrAddr, targetAddr, instrLength));

        public static int GetRelOffset(long srcInstrAddr, long targetAddr, int instrLength = 0)
            => (int)(targetAddr - (srcInstrAddr + instrLength));

        public static byte[] GetRelOffsetBytes(long srcInstrAddr, long targetAddr, int instrLength = 0)
            => BitConverter.GetBytes(GetRelOffset(srcInstrAddr, targetAddr, instrLength));

        public static void WriteRelativeOffsets(byte[] bytes,
            (long baseAddr, long targetAddr, int size, int destinationIndex)[] offsets)
        {
            foreach (var (baseAddr, targetAddr, size, destinationIndex) in offsets)
            {
                var relativeBytes = GetRelOffsetBytes(baseAddr, targetAddr, size);
                Array.Copy(relativeBytes, 0, bytes, destinationIndex, 4);
            }
        }

        public static byte[] GetJmpOriginOffsetBytes(long hookLocation, int originalInstrLen, IntPtr customCodeEnd)
            => BitConverter.GetBytes((int)(hookLocation + originalInstrLen - customCodeEnd.ToInt64()));

        public static void WriteJumpOffsets(byte[] bytes,
            (long hookLocation, int originalInstrLen, IntPtr customCodeAddr, int destinationIndex)[] jumpOffsets)
        {
            foreach (var (hookLocation, originalInstrLen, customCodeAddr, destinationIndex) in jumpOffsets)
            {
                var originOffsetBytes = GetJmpOriginOffsetBytes(hookLocation, originalInstrLen, customCodeAddr + 5);
                Array.Copy(originOffsetBytes, 0, bytes, destinationIndex, 4);
            }
        }
        
        public static byte[] GetAbsAddressBytes(long address)
            => BitConverter.GetBytes(address);
        
        public static void WriteAbsoluteAddresses(byte[] bytes, (long address, int destinationIndex)[] addresses)
        {
            foreach (var (address, destinationIndex) in addresses)
            {
                var addressBytes = GetAbsAddressBytes(address);
                Array.Copy(addressBytes, 0, bytes, destinationIndex, 8);
            }
        }
    }
}