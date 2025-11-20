using EldenRingTool.Util;
using MiscUtils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using static EldenRingTool.ERProcess;

namespace EldenRingTool
{
    public class ERProcess : IDisposable
    {
        public const uint PROCESS_ALL_ACCESS = 2035711;
        private Process _targetProcess = null;
        private IntPtr _targetProcessHandle = IntPtr.Zero;
        public IntPtr erBase = IntPtr.Zero;
        int erSize = 0;

        protected bool disposed = false;

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAcess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int iSize, ref int lpNumberOfBytesRead);

        [DllImport("ntdll.dll")]
        static extern int NtReadVirtualMemory(IntPtr ProcessHandle, IntPtr BaseAddress, byte[] Buffer, UInt32 NumberOfBytesToRead, ref UInt32 NumberOfBytesRead); //consider replacing ReadProcessMemory with this

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int iSize, int lpNumberOfBytesWritten);

        [DllImport("ntdll.dll")]
        static extern int NtWriteVirtualMemory(IntPtr ProcessHandle, IntPtr BaseAddress, byte[] Buffer, UInt32 NumberOfBytesToWrite, ref UInt32 NumberOfBytesWritten); //faster alternative to WriteProcessMemory

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        public const uint MemCommit = 0x1000;
        public const uint MemReserve = 0x2000;
        public const uint PageExecuteReadwrite = 0x40;
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
            uint flAllocationType = MemCommit | MemReserve,
            uint flProtect = PageExecuteReadwrite
        );

        public uint RunThread(IntPtr address, uint timeout = 0xFFFFFFFF, IntPtr? param = null)
        {
            var thread = CreateRemoteThread(_targetProcessHandle, IntPtr.Zero, 0, address, param ?? IntPtr.Zero, 0, IntPtr.Zero);
            var ret = WaitForSingleObject(thread, timeout);
            CloseHandle(thread); //return value unimportant
            return ret;
        }

        Thread freezeThread = null;
        bool _running = true;
        HookManager _hookManager;
        public ERProcess()
        {

            _hookManager = new HookManager(this);
            findAttach();
            findBaseAddress();
            aobScan();

            freezeThread = new Thread(() => { freezeFunc(); });
            freezeThread.Start();
        }

        public void Dispose()
        {
            if (!disposed)
            {
                _running = false;
                if (freezeThread != null)
                {
                    freezeThread.Abort();
                    freezeThread = null;
                }
                detach();
                disposed = true;
            }
        }

        ~ERProcess()
        {
            Dispose();
        }

        const string erProNameMain = "eldenring";
        const string erProNameAlt = "start_protected_game"; //alternate EAC bypass trick is just an exe rename
        string erProName = "";

        public static bool checkGameRunning()
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                if (process.ProcessName.ToLower().Equals(erProNameMain.ToLower()) && !process.HasExited)
                {
                    return true;
                }
                if (process.ProcessName.ToLower().Equals(erProNameAlt.ToLower()) && !process.HasExited)
                {
                    return true;
                }
            }
            return false;
        }

        private void findAttach()
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                if (process.ProcessName.ToLower().Equals(erProNameMain.ToLower()) && !process.HasExited)
                {
                    attach(process);
                    erProName = erProNameMain;
                    return;
                }
                if (process.ProcessName.ToLower().Equals(erProNameAlt.ToLower()) && !process.HasExited)
                {
                    attach(process);
                    erProName = erProNameAlt;
                    return;
                }
            }
            throw new Exception("Elden Ring not running");
        }

        private void attach(Process proc)
        {
            if (_targetProcessHandle == IntPtr.Zero)
            {
                _targetProcess = proc;
                _targetProcessHandle = OpenProcess(PROCESS_ALL_ACCESS, bInheritHandle: false, _targetProcess.Id);
                if (_targetProcessHandle == IntPtr.Zero)
                {
                    throw new Exception("Attach failed");
                }
            }
            else
            {
#if NO_WPF
                Console.WriteLine("Already attached");
#else
                System.Windows.MessageBox.Show("Already attached");
#endif
            }
        }

        private void findBaseAddress()
        {
            try
            {
                foreach (var module in _targetProcess.Modules)
                {
                    var processModule = module as ProcessModule;
                    //Utils.debugWrite(processModule.ModuleName);
                    var modNameLower = processModule.ModuleName.ToLower();
                    if (modNameLower == erProName + ".exe")
                    {
                        erBase = processModule.BaseAddress;
                        erSize = processModule.ModuleMemorySize;
                    }
                }
            }
            catch (Exception ex) { Utils.debugWrite(ex.ToString()); }
            if (erBase == IntPtr.Zero)
            {//this is pretty unlikely
                throw new Exception("Couldn't find ER base address");
            }
        }

        private void detach()
        {
            if (!(_targetProcessHandle == IntPtr.Zero))
            {
                _targetProcess = null;
                try
                {
                    CloseHandle(_targetProcessHandle);
                    _targetProcessHandle = IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    Utils.debugWrite(ex.ToString());
                }
            }
        }

        //all read/write funcs just fail silently, except this one:
        public bool ReadTest(IntPtr addr)
        {
            var array = new byte[1];
            var lpNumberOfBytesRead = 1;
            return ReadProcessMemory(_targetProcessHandle, addr, array, 1, ref lpNumberOfBytesRead) && lpNumberOfBytesRead == 1;
        }

        public int ReadInt32(IntPtr addr)
        {
            var bytes = ReadBytes(addr, 4);
            return BitConverter.ToInt32(bytes, 0);
        }

        public long ReadInt64(IntPtr addr)
        {
            var bytes = ReadBytes(addr, 8);
            return BitConverter.ToInt64(bytes, 0);
        }

        public byte ReadUInt8(IntPtr addr)
        {
            var bytes = ReadBytes(addr, 1);
            return bytes[0];
        }

        public uint ReadUInt32(IntPtr addr)
        {
            var bytes = ReadBytes(addr, 4);
            return BitConverter.ToUInt32(bytes, 0);
        }

        public ulong ReadUInt64(IntPtr addr)
        {
            var bytes = ReadBytes(addr, 8);
            return BitConverter.ToUInt64(bytes, 0);
        }

        public float ReadFloat(IntPtr addr)
        {
            var bytes = ReadBytes(addr, 4);
            return BitConverter.ToSingle(bytes, 0);
        }

        public double ReadDouble(IntPtr addr)
        {
            var bytes = ReadBytes(addr, 8);
            return BitConverter.ToDouble(bytes, 0);
        }

        public byte[] ReadBytes(IntPtr addr, int size)
        {
            var array = new byte[size];
            var targetProcessHandle = _targetProcessHandle;
            var lpNumberOfBytesRead = 1;
            ReadProcessMemory(targetProcessHandle, addr, array, size, ref lpNumberOfBytesRead);
            return array;
        }

        public void WriteInt32(IntPtr addr, int val)
        {
            WriteBytes(addr, BitConverter.GetBytes(val));
        }

        public void WriteUInt32(IntPtr addr, uint val)
        {
            WriteBytes(addr, BitConverter.GetBytes(val));
        }

        public void WriteFloat(IntPtr addr, float val)
        {
            WriteBytes(addr, BitConverter.GetBytes(val));
        }

        public void WriteUInt8(IntPtr addr, byte val)
        {
            var bytes = new byte[] { val };
            WriteBytes(addr, bytes);
        }

        public void WriteBytes(IntPtr addr, byte[] val, bool useNewWrite = true)
        {
            if (useNewWrite)
            {
                uint written = 0;
                NtWriteVirtualMemory(_targetProcessHandle, addr, val, (uint)val.Length, ref written); //MUCH faster, <1ms
            }
            else
            {
                WriteProcessMemory(_targetProcessHandle, addr, val, val.Length, 0); //can take as long as 15ms!
            }
        }

        public enum DebugOpts
        {
            COL_MESH_A, COL_MESH_B,
            CHARACTER_MESH,
            HITBOX_VIEW_A, HITBOX_VIEW_B,
            DISABLE_MOST_RENDER,
            DISABLE_MAP,
            DISABLE_TREES,
            DISABLE_ROCKS,
            DISABLE_DISTANT_MAP,
            DISABLE_CHARACTER,
            DISABLE_GRASS,
            NO_DEATH, ALL_CHR_NO_DEATH,
            INSTANT_QUITOUT,
            ONE_HP,
            MAX_HP, RUNE_ARC,
            TARGET_HP,
            DISABLE_AI, NO_STAM, NO_FP, NO_GOODS, NO_ARROWS, ONE_SHOT,
            NO_GRAVITY_ALTERNATE, NO_MAP_COLLISION, NO_GRAVITY,
            TORRENT_NO_DEATH, TORRENT_NO_GRAV_ALT, TORRENT_NO_MAP_COLL, TORRENT_NO_GRAV,
            TOP_DEBUG_MENU,
            POISE_VIEW,
            TARGETING_VIEW,
            EVENT_DRAW, EVENT_STOP,
            FREE_CAM,
            DISABLE_STEAM_INPUT_ENUM, DISABLE_STEAM_ACHIVEMENTS,
        }

        public enum TargetInfo
        {
            HP, MAX_HP,
            POISE, MAX_POISE, POISE_TIMER,
            POISON, POISON_MAX, //resists must match memory order
            ROT, ROT_MAX,
            BLEED, BLEED_MAX,
            BLIGHT, BLIGHT_MAX,
            FROST, FROST_MAX,
            SLEEP, SLEEP_MAX,
            MADNESS, MADNESS_MAX,
            STANDARD, SLASH, STRIKE, PIERCE,
            MAGIC, FIRE, LIGHTNING, HOLY,
        }

        public enum ItemCategory 
        { 
            NONE,
            SMITHING,
            SOMBER
        }

        const long SANE_MINIMUM = 0x700000000000;
        const long SANE_MAXIMUM = 0x800000000000; //TODO: refine. much lower addresses may be valid in some cases.

        //addresses/offsets - all are for 1.06 but will be replaced by AOB scanning at run time

        int worldChrManOff = 0x3C310B8; //pointer to CS::WorldChrManImp

        int hitboxBase = 0x3C31488; //currently no RTTI name for this.
        uint hitboxOffset = 0xA0;

        int groupMaskBase = 0x3A1E830;//most render
        //const int groupMaskMap = 0x3A1E831;
        int groupMaskTrees = 0x3A1E839;

        int meshesOff = 0x3C3518C;//static addresses again

        int quitoutBase = 0x3C349D8; //CS::GameMan.

        int logoScreenBase = 0xA9807D;

        int codeCavePtrLoc = 0x25450;
        int targetHookLoc = 0x6F89A2;
        int targetHookOffset = 0x6A0;

        int codeCaveCodeLoc { get { return codeCavePtrLoc + 0x10; } }

        List<int> menuOffsets = new List<int>();

        int noGoodsConsume = 0x3C312B3;
        int noAiUpdate = 0x3C312BF;

        int chrDbg = 0x3C312A8;//should be close to misc debug

        int newMenuSystem = 0x3C369A0;//CS::CSMenuManImp //irrelvant now with top debug gone

        int fontDrawOffset = 0x25EAF20; //defaults to 0x48. need 0xC3 for in-game poise viewer.

        int DbgEventManOff = 0x3C330C0; //no name. static addresses.

        int EventPatchLoc1 = 0xDC8670; //32 C0 C3 (next 3 vary with patch, eg. CC BF 60 in 1.03.2, cc 7b 83 in 1.04.0)
        int EventPatchLoc2 = 0xDC8650; //32 C0 C3 (next 3 vary with patch, eg. CC E3 A2 in 1.03.2, 90 49 8b in 1.04.0)

        int FieldAreaOff = 0x3C34298;//CS::FieldArea

        //int freeCamPatchLoc = 0x415305;
        int freeCamPatchLocAlt = 0xDB8A00; //1st addr after call (a jmp)

        int freeCamPlayerControlPatchLoc = 0x664EE6;

        int mapOpenInCombatOff = 0x7CB4D3;
        int mapStayOpenInCombatOff = 0x979AE7;

        //DbgGetForceActIdx. patch changes it to use the addr from DbgSetLastActIdx 
        int enemyRepeatActionOff = 0x4F22456;

        int zeroCaveOffset = 0x28E3E00; //zeroes at the end of the program
        int warpFirstCallOffset = 0x5DDE30;
        int warpSecondCallOffset = 0x65E260;

        int itemSpawnStart { get { return zeroCaveOffset + 0x100; } } //warp is only 0x3E big but just go for a round number
        int mapItemManOff = 0x3C32B20;
        int itemSpawnCall = 0x5539E0;
        int itemSpawnData { get { return itemSpawnStart + 0x30; } }

        int usrInputMgrImplOff = 0x45075C8;//DLUID::DLUserInputManagerImpl<DLKR::DLMultiThreadingPolicy> //RTTI should find it
        uint usrInputMgrImpSteamInputFlagOff = 0x88b; //in 1.05, the func checking the flag is at +1E7D75F
        //above originally found by putting breakpoints in user32 device enum funcs, which get called by dinput8, which gets called by the steam overlay dll, which gets called by elden ring, then triggering the stutter.

        int trophyImpOffset = 0x4453838; //CS::CSTrophyImp

        int gameDataMan = 0;

        int csFlipperOff = 0x4453E98; //lots of interesting stuff here. frame times, fps, etc.
        int gameSpeedOffset = 0x2D4;
        int frameTimeTargetOffset = 0xE44832; //value for 2.01

        int upgradeRuneCostOff = 0x765241;
        int upgradeMatCostOff = 0x8417FC;

        int soundDrawPatchLoc = 0x33bfd6;

        int allTargetingDebugDraw = 0x3C2D43A;

        int allChrNoDeath = 0x3C312BA;

        int torrentDisabledCheckOne = 0xC730EA;
        int torrentDisabledCheckTwo = 0x6E7CDF;

        //both of these are equivalent. not sure why different tables use different ones. perhaps one is more likely to survive future patches.
        //uint worldChrManPlayerOff1 = 0xB658; //points to a pointer to CS::PlayerIns. constant not found...? likely less reliable.
        uint worldChrManPlayerOff2 = 0x18468; //points directly to CS::PlayerIns, commonly found after refs to CS::WorldChrManImp

        //uint worldChrManTorrentOff = 0x18378; //changed in 1.06. need AOB for this. cannot find the constant in the game however so it likely has a different way to get to torrent.
        //above method works in 1.07, base ptr is 0x1e1a0: 0x1ded8 + 59*8
        //uint worldChrManTorrentOffAlt = 0xb6f0; //works up to 1.06. broken in 1.07.

        uint noDeathOffset = 0x19B; //was 197 in an older patch

        uint mapIDinPlayerIns = 0x6C0;

        uint chrSetOffset = 0x1DED8; //1.07 addr, was stable previously
        uint pgDataOffset = 0x570; //patch stable so far
        uint torrentIDOffset = 0x930; //also appears patch stable

        int scadOffset = 0; //should be 0xfc or close to it
        public bool exeSupportsDlc() { return scadOffset > 0 && scadOffset < 0x10000; } //if no plausible value is found then the exe is too old

        int musicMuteLoc = 0;

        int csEventFlagMan = 0;

        int triggerNGPlusOffset = 0; //in GameMan

        private int _hasSpEffectHook;
        private int _infinitePoiseHook;
        private int _blueTargetViewHook;



        //scanning for above addresses
        void aobScan()
        {//see https://github.com/kh0nsu/FromAobScan
            var sw = new Stopwatch();
            sw.Start();
            var scanner = new AOBScanner(_targetProcessHandle, erBase, erSize);

            worldChrManOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 4C 8B A8 ?? ?? ?? ?? 4D 85 ED 0F 84 ?? ?? ?? ??", "CS::WorldChrManImp", 5 + 3, 5 + 3 + 4, startIndex: 1800000);
            worldChrManPlayerOff2 = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 4C 8B A8 ?? ?? ?? ?? 4D 85 ED 0F 84 ?? ?? ?? ??", "CS::WorldChrManImp offset", 5 + 7 + 3, startIndex: 1800000);
            hitboxBase = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B0D ???????? E8 ???????? F3 0F1057 ?? 48 8BCB 0FB693 ????0000 E8 ???????? 48 8BCB E8 ???????? 48 8B0D ????????", "hitboxBase", 1 + 2, 1 + 2 + 4, startIndex: 10900000);
            hitboxOffset = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "80B9 ????0000 00 48 8BF9 BE FFFFFFFF 74 ?? 48 8B19 48 85DB 74 ??", "hitboxOffset", 2, startIndex: 5200000);

            groupMaskBase = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "803D ???????? 00 0F1000 0F1145 D0 0F84 ????0000 803D ???????? 00 0F85 ????0000 803D ???????? 00 B3 01 C605 ???????? 00 74 ?? BA 01000000", "groupMaskBase", 2, 2 + 4 + 1, startIndex: 6500000);
            groupMaskTrees = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "803D ???????? 00 74 1f ba 05000000", "groupMaskTrees", 2, 7, startIndex: 6500000);
            meshesOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "0F B6 25 ?? ?? ?? ?? 44 0F B6 3D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B6 F8", "meshesOff", 3, 7, startIndex: 7100000);

            quitoutBase = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 05 ?? ?? ?? ?? 0F B6 40 10 C3", "CS::GameMan", 3, 7, startIndex: 6500000);
            logoScreenBase = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "74 53 48 8B05 ???????? 48 85C0 75 ?? 48 8D0D ???????? E8 ???????? 4C 8BC8 4C 8D05 ???????? BA ????0000 48 8D0D ???????? E8 ????????", "logoScreenBase", startIndex: 10900000);
            var thl = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B48 ?? 49 898D ????0000 49 8BCE E8 ???????? 84C0 75 ?? 49 8B5E ?? 48 8D4D ?? E8 ????????", "targetHookLoc", startIndex: 7100000);
            if (thl > 0) { targetHookLoc = thl; } //should do this with all patches really, as they will fail the scan if already patched
            var tho = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B48 ?? 49 898D ????0000 49 8BCE E8 ???????? 84C0 75 ?? 49 8B5E ?? 48 8D4D ?? E8 ????????", "targetHookLoc offset", 1 + 2 + 1 + 1 + 2, startIndex: 7100000);
            if (tho > 0) { targetHookOffset = tho; }
            noGoodsConsume = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "803D ???????? 00 75 05 45 33C0 EB 03 41 B0 01 48 8D8424 ??000000 48 898424 ??000000 41 8B06 898424 ??000000 48 8D8E ????0000 48 8D9424 ??000000 E8 ???????? 0FB6D0", "noGoodsConsume", 2, 2 + 4 + 1, startIndex: 6300000);
            noAiUpdate = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "0FB63D ???????? 48 85C0 75 2E", "noAIUpdate", 3, 7, startIndex: 3800000);

            chrDbg = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 05 ?? ?? ?? ?? 41 83 FF 02 ?? ?? 48 85 C0", "chrDbg", 3, 7, startIndex: 5000000);
            newMenuSystem = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B05 ???????? 33DB 48 897424 ?? 48 8BF1 895C24 ?? 48 85C0 75 ??", "CSMenuManImp", 3, 7, startIndex: 7400000);
            fontDrawOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 895C24 10 55 56 57 41 54 41 55 41 56 41 57 48 8D6C24 ?? 48 81EC ???????? 0F29B424 ????????", "fontDrawOffset", startIndex: 39000000);
            DbgEventManOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 0D ???????? 48 85 C9 ???? 83 CF 20", "DbgEventManOff", 3, 7, startIndex: 10000000);

            EventPatchLoc1 = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "E8 ???????? 84C0 74 06 E8 ???????? 90 48 8BC7", "Event patch func 1", 1, 1 + 4, startIndex: 5500000);
            EventPatchLoc2 = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "E8 ???????? 84C0 74 06 E8 ???????? 90 48 8BC7", "Event patch func 2", 1 + 4 + 2 + 1 + 1 + 1, 1 + 4 + 2 + 1 + 1 + 1 + 4, startIndex: 5500000);
            FieldAreaOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 0D ?? ?? ?? ?? 48 ?? ?? ?? 44 0F B6 61 ?? E8 ?? ?? ?? ?? 48 63 87 ?? ?? ?? ?? 48 ?? ?? ?? 48 85 C0", "CS::FieldArea", 3, 7, startIndex: 6400000);
            freeCamPatchLocAlt = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "EB 05 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 75 0C", "free cam patch loc, 1st called addr", readoffset32: 1 + 1 + 1 + 4 + 1, 1 + 1 + 1 + 4 + 1 + 4, startIndex: 7000000);

            freeCamPlayerControlPatchLoc = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "8B 83 ?? 00 00 00 FF C8 83 F8 01", "free cam player control patch loc", startIndex: 6500000);
            if (freeCamPlayerControlPatchLoc < 0)
            {
                freeCamPlayerControlPatchLoc = scanner.findAddr(scanner.sectionTwo, scanner.textTwoAddr, "8B 83 ?? 00 00 00 FF C8 83 F8 01", "free cam player control patch loc (2nd section)");
                if (freeCamPlayerControlPatchLoc < 0)
                {//ugh
                    freeCamPlayerControlPatchLoc = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "8B 93 c8 00 00 00 85 d2 0f ?? ?? ?? 00 00", "free cam player control patch loc 1.12", startIndex: 6500000);
                }
            }
            mapOpenInCombatOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "E8 ???????? 84C0 74 ?? C745 ?? ???????? C745 ?? ???????? C745 ?? ???????? 48 8D05 ????????", "map open in combat", startIndex: 8000000);
            mapStayOpenInCombatOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "E8 ?? ?? ?? ?? 84 C0 75 ?? 38 83 ?? ?? 00 00 75 ?? 83 ?? fe 89", "map stay open in combat", startIndex: 9800000);

            enemyRepeatActionOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 41 08 0F BE 80 ?? E9 00 00", "enemyRepeatActionOff (1st sect)", justOffset: 7);
            if (enemyRepeatActionOff < 0)
            {
                enemyRepeatActionOff = scanner.findAddr(scanner.sectionTwo, scanner.textTwoAddr, "48 8B 41 08 0F BE 80 ?? E9 00 00", "enemyRepeatActionOff (2nd sect)", justOffset: 7);
            }
            warpFirstCallOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 83EC 48 48 C74424 28 FEFFFFFF E8 ?? ?? ?? ?? 48", "warp call one", startIndex: 6000000);
            warpSecondCallOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "488B05 ???????? 8988 ??0C0000 C3", "warp call two", startIndex: 6500000);
            usrInputMgrImplOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8905 ???????? 48 8B05 ???????? E8 ???????? 4C 8B08 41 B8 ??000000 48 8D15 ????0000 48 8BC8 41 FF51 ?? 48 8B1D ????????", "usrInputMgrImplOff", 3, 7, startIndex: 1000000);
            usrInputMgrImpSteamInputFlagOff = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "80B9 ????0000 00 48 8B5C24 40", "steam input flag check", 2, startIndex: 31500000);
            csFlipperOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 05 ?? ?? ?? ?? F3 0F 10 88 ?? ?? ?? ?? F3 0F", "csFlipperOff", 3, 7, startIndex: 13800000);
            gameSpeedOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 05 ?? ?? ?? ?? F3 0F 10 88 ?? ?? ?? ?? F3 0F", "csFlipperOff gameSpeedOffset", 7 + 4, startIndex: 13800000);
            frameTimeTargetOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "8973 ?? C743 ?? ??88883C EB ?? 8973 ??", "frame time target (1/60.0f)", justOffset: 6, startIndex: 13000000);

            noDeathOffset = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "4883EC20F681????000001488bd97408", "no-death offset in CSChrDataModule", 4 + 2, startIndex: 4200000);
            gameDataMan = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 05 48 8B 40 58 C3 C3", "GameDataMan", 3, 7, startIndex: 2000000);

            trophyImpOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48833D ???????? 00 75 31 4C 8B05 ???????? 4C 8945 10 BA 08000000 8D4A 18", "CS::CSTrophyImp", 3, 8, startIndex: 13800000);

            upgradeRuneCostOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "74 28 48 8B45 ?? 48 85C0 74 07 F3 0F1048 ?? EB 08 F3 0F100D ????????", "Weapon upgrade rune cost", startIndex: 7500000);
            upgradeMatCostOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "8BF8 44 8BC3 48 8D55 ?? 48 8D4D ?? E8 ????????", "Weapon upgrade material cost", startIndex: 8500000);

            soundDrawPatchLoc = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "74 ?? 48 8B0D ???????? BE 01000000 897424 ?? 48 85C9 75 ?? 48 8D0D ???????? E8 ????????", "soundDrawPatchLoc", startIndex: 3200000);

            allTargetingDebugDraw = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "40 3835 ???????? 0F84 ????0000 48 8D5424 ?? 48 8BCF E8 ????0000 48 8D4C24 ?? E8 ???????? 6644 85BF ????000074 ?? 48 8B05 ???????? 48 85C0 75 ?? 48 8D0D ???????? E8 ???????? 4C 8BC8 4C 8D05 ????????BA ????0000 48 8D0D ????????E8 ???????? 48 8B05 ????????48 8B80 ????????48 8D5424 ?? 48 8B88 ????????48 8B49 ?? E8 ???????? EB ?? 8B8F ????0000 E8 ???????? F3 0F1145 ?? 48 8D4C24 ?? 66 859F ????000074 ?? B2 ?? EB ??", "allTargetingDebugDraw", 3, 3 + 4, startIndex: 3200000); //yes, it's long, take off more than a little and it gets two matches

            mapItemManOff = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B 0D ???????? C7 44 24 50 FFFFFFFF", "MapItemManImpl", 3, 7, startIndex: 5700000);
            itemSpawnCall = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "8B 02 83 F8 0A", "ItemSpawnCall", justOffset: -0x52, startIndex: 5400000);
            if (itemSpawnCall < 0)
            {
                itemSpawnCall = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "40 55 56 57 41 54 41 55 41 56 41 57 48 8D6C24 ?? 48 81EC ????0000 48 C745 ?? FEFFFFFF 48 899C24 ????0000 48 8B05 ???????? 48 33C4 48 8945 ?? 44 894C24 ?? 4D 8B??4C 894424 ?? 4C 8B??33FF 897C24 ?? 8B02 83F8 ?? 0F87 ????0000", "ItemSpawnCall 1.02.x", startIndex: 5400000);
            }

            allChrNoDeath = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "803D ???????? 00 75 ?? 48 8BCB E8 ???????? 48 8BC8 E8 ???????? 84C0 74 ?? 48 833D ???????? 00", "allChrNoDeath", 2, 2 + 4 + 1, startIndex: 4200000);
            //scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "803D ???????? 00 0F85 ???????? 32C0 48 83C4 20 5B C3", "playerNoDeath", 2, 2 + 4 + 1, startIndex: 4200000);

            torrentDisabledCheckOne = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B40 68 8078 36 00 0F95C0 40 B7 01 8806 48 8B5C24 30 40 0FB6C7 48 8B7424 38 48 83C4 20 5F C3", "torrentDisabledCheckOne", justOffset: 4 + 4, startIndex: 12900000);
            torrentDisabledCheckTwo = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "E8 ???????? 48 8B48 ?? 8079 36 00 0F95C0 48 83C4 ?? C3", "torrentDisabledCheckTwo", justOffset: 5 + 4 + 4, startIndex: 7000000);

            mapIDinPlayerIns = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "C783 ????0000 FFFFFFFF 0F280D ???????? 66 0F7F4D ?? F2 0F118B ????0000 66 0F73D9 ?? 66 0F7E8B", "mapIDinPlayerIns", readoffset32: 2, startIndex: 6300000);

            chrSetOffset = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B8CFE ??????00 48 85C9 74 ?? 4C 8B01 8BD0 41 FF50 ?? 48 8B7C24 ?? 48 8B5C24 ?? 48 83C4 ??", "worldChrManChrSetOffset", 4, startIndex: 5000000);
            pgDataOffset = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B81 ????0000 48 C702 FFFFFFFF 48 85C0 74 0A 48 8B80 ????0000 48 8902 48 8BC2", "PGDataOffset", 3, startIndex: 6400000);
            torrentIDOffset = (uint)scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8B81 ????0000 48 C702 FFFFFFFF 48 85C0 74 0A 48 8B80 ????0000 48 8902 48 8BC2", "TorrentIDOffset", 3 + 4 + 3 + 4 + 3 + 2 + 3, startIndex: 6400000);

            scadOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "80 b9 ?? ?? 00 00 00 74 08 0f b6 81 ?? ?? 00 00 c3 0f b6 81 ?? ?? 00 00 c3", "CS::PlayerGameData::GetScadutreeBlessing (scadu offset in pgdata)", readoffset32: 20, startIndex: 2000000);

            musicMuteLoc = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "0f b6 48 04 0f 29 74 24 70 0f 57 f6 0f 29 7c 24 60", "Music volume read (patch 31C99090 to mute)", startIndex: 13000000);

            scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "4c 8b dc 53 56 57 48 81 ec 90 00 00 00 49 c7 43 88 fe ff ff ff 48 8b d9", "bonfire menu (6 matches)", startIndex: 7500000, singleMatch: false,
                callback: x => menuOffsets.Add(x));
            scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "4c 8b dc 53 48 81 ec 90 00 00 00 49 c7 43 88 fe ff ff ff 48 8b 05 ?? ?? ?? ?? 48 33 c4 48 89 84 24 80 00 00 00 48 8b d9 49 c7 43 e0 00 00 00 00 49 8d 43 a8 49 89 43 90 49 8d 43 a8 49 89 43 98 48 8d 05 ?? ?? ?? ?? 49 89 43 a8 48 8d 05 ?? ?? ?? ?? 49 89 43 a8 48 8d 05 ?? ?? ?? ?? 49 89 43 b0", "three more menus", startIndex: 7500000, singleMatch: false,
            callback: x => menuOffsets.Add(x));
            csEventFlagMan = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 833D ???????? 00 0F84 ????0000 44 8BE6 85 C0 0F 84 ????0000", "GLOBAL_CSEventFlagMan", 1 + 2, 1 + 2 + 4 + 1, startIndex: 2000000);

            triggerNGPlusOffset = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "48 8b 05 ?? ?? ?? ?? 0f b6 80 ?? ?? 00 00 c3 ?? 48 8b 05 ?? ?? ?? ?? 8b 90", "Trigger NG+ offset in GameMan", readoffset32: 3 + 4 + 3, startIndex: 6000000);


            _hasSpEffectHook = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "39 51 08 74 0C 48 8B", "HasSpEffect", justOffset: -0x10);
            _blueTargetViewHook = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "0F 84 41 01 00 00 48 8D 54", "BlueTargetViewHook", justOffset: 0x6);
            _infinitePoiseHook = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, "80 BF 5F 02", "InfinitePoise", justOffset: 0);

            var cave = "";
            for (int i = 0; i < 0xA0; i++) { cave += "90"; }
            codeCavePtrLoc = scanner.findAddr(scanner.sectionOne, scanner.textOneAddr, cave, "codeCave_0x60_nops", startIndex: 100000);

            zeroCaveOffset = scanner.textOneAddr + (int)scanner.textOneSize;

            scanner.Dispose();

            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds);
        }

        //code templates and patches - should be game version independent

        static readonly byte[] itemSpawnTemplate = new byte[]
        {
            0x48, 0x83, 0xEC, 0x48, //sub rsp,48
            0x4D, 0x31, 0xC9, //xor r9,r9
            0x4C, 0x8D, 0x05, 0x22, 0x00, 0x00, 0x00, //lea r8,[eldenring.exe+28E3F30] - relative address, won't change
            0x49, 0x8D, 0x50, 0x20, //lea rdx,[r8+20]
            0x49, 0xBA, 0, 0, 0, 0, 0, 0, 0, 0, //mov r10,MapItemMan (addr offset 0x14)
            0x49, 0x8B, 0x0A, //mov rcx,[r10]
            0xE8, 0, 0, 0, 0, //call itemSpawnCall. itemSpawnCall - (itemSpawnStart + 0x24) (addr offset 0x20)
            0x48, 0x83, 0xC4, 0x48, //add rsp,48
            0xC3, //ret
        };

        static readonly byte[] warpCodeTemplate = new byte[]
        {
            0x00, 0x00, 0x00, 0x00,                   // id (seems pointless)
            0x48, 0x83, 0xEC, 0x48,                   // sub rsp,48 (func start)
            0xB9, 0xAA, 0x00, 0x00, 0x00,             // mov ecx,000000AA
            0xBA, 0xBB, 0x00, 0x00, 0x00,             // mov edx,000000BB
            0x41, 0xB8, 0xCC, 0x00, 0x00, 0x00,       // mov r8d,000000CC
            0x41, 0xB9, 0xDD, 0x00, 0x00, 0x00,       // mov r9d,000000DD
            0x48, 0x8D, 0x05, 0xDB, 0xFF, 0xFF, 0xFF, // lea rax,[eldenring.exe+zeroCaveOffset] (relative addr)
            0x48, 0x89, 0x44, 0x24, 0x20,             // mov [rsp+20],rax
            0xE8, 0, 0, 0, 0,                         //call to pack coords+warp (to be filled in)
            0xB9, 0x00, 0x00, 0x00, 0x00,             // mov ecx,00000000
            0xE8, 0, 0, 0, 0,                         //call set warp coords (with 0 to clear) (to be filled in)
            0x48, 0x83, 0xC4, 0x48,                   // add rsp,48
            0xC3,                                     // ret 
        };

        readonly byte[] torrentCheckOrigBytes = { 0x0F, 0x95, 0xC0 };
        readonly byte[] torrentCheckPatchBytes = { 0x30, 0xC0, 0x90 };

        static readonly byte[] targetHookOrigCode = new byte[] { 0x48, 0x8B, 0x48, 0x08, 0x49, 0x89, 0x8D, 0, 0, 0, 0, }; //last four bytes are the 'target hook offset' which varies with patches. followed by 0x49, 0x8B, 0xCE, 0xE8, which stays unchanged.
        static readonly byte[] targetHookReplacementCodeTemplate = new byte[] { 0xE9,
            0, 0, 0, 0, //address offset
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
        //replacement code contains the offset from the following instruction (basically hook loc + 5) to the code cave.
        //then it just nops to fill out the rest of the old instructions
        byte[] getTargetHookReplacementCode()
        {
            var ret = new byte[targetHookReplacementCodeTemplate.Length];
            int addrOffset = codeCaveCodeLoc - (targetHookLoc + 5); //target minus next instruction location (ie. the NOP 5 bytes in)
            Array.Copy(targetHookReplacementCodeTemplate, ret, ret.Length);
            Array.Copy(BitConverter.GetBytes(addrOffset), 0, ret, 1, 4);
            return ret;
        }

        static readonly byte[] targetHookCaveCodeTemplate = new byte[] { 0x48, 0xA3,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, //full 64 bit ptr address goes here
        0x48, 0x8B, 0x48, 0x08, //should be identical to orig code from here to just before E9
        0x49, 0x89, 0x8D, 0, 0, 0, 0, //fill in offset from scan
        0xE9,
        0, 0, 0, 0, //address offset
         };

        readonly byte[] freeCamPlayerControlPatchOrig = new byte[] { 0x8B, 0x83, 0xC8, 0, 0, 0 }; //C8 may need to change in different patches
        readonly byte[] freeCamPlayerControlPatchReplacement = new byte[] { 0x31, 0xC0, 0x90, 0x90, 0x90, 0x90 };

        readonly byte[] freeCamPlayerControlPatchOrig112 = new byte[] { 0x8B, 0x93, 0xC8, 0, 0, 0 }; //MOV EDX,dword ptr[RBX + 0xc8]
        readonly byte[] freeCamPlayerControlPatchReplacement112 = new byte[] { 0x31, 0xD2, 0x90, 0x90, 0x90, 0x90 }; //xor edx,edx, nop nop nop nop


        readonly byte[] logoScreenOrig = new byte[] { 0x74, 0x53 };
        readonly byte[] logoScreenPatch = new byte[] { 0x90, 0x90 };

        readonly byte[] eventPatch = new byte[] { 0xB0, 0x01 };

        readonly byte[] soundDrawOrigBytes = { 0x74, 0x53 }; //JZ +53
        readonly byte[] soundDrawPatchBytes = { 0x90, 0x90 }; //nop nop

        //readonly byte[] freeCamOrigCode = new byte[] { 0x32, 0xC0 };
        //readonly byte[] freeCamPatchCode = new byte[] { 0xB0, 0x01 };
        readonly byte[] freeCamPatchCodeAlt = new byte[] { 0xB0, 0x01, 0xC3 }; //return 1

        readonly byte[] mapCombatCheckPatchCode = new byte[] { 0x31, 0xC0, 0x90, 0x90, 0x90 }; //xor eax, eax; nop nop nop

        byte fontDrawNewValue = 0xC3; //ret

        const byte enemyRepeatActionPatchVal = 0xB2;
        const byte enemyRepeatActionOrigVal = 0xB1;

        const byte enemyRepeatActionPatchVal108 = 0xC2;
        const byte enemyRepeatActionOrigVal108 = 0xC1;

        readonly byte[] musicMuteOrig = { 0x0f, 0xb6, 0x48, 0x04 }; //MOVZX ECX,byte ptr[RAX + 0x4]
        readonly byte[] musicMutePatch = { 0x31, 0xC9, 0x90, 0x90 }; //xor ecx,ecx; nop; nop

        //patch functions, helper functions, etc.

        //we have another way to get this, but can use this as a fallback.
        /*IntPtr getPlayerGameDataPtr()
        {
            var ptr = ReadUInt64(erBase + gameDataMan);
            var ptr2 = ReadUInt64((IntPtr)ptr + 8); //CS::PlayerGameData
            return (IntPtr)ptr2;
        }*/

        byte[] getTargetHookCaveCodeTemplate()
        {
            var ret = new byte[targetHookCaveCodeTemplate.Length];
            int addrOffset = targetHookLoc + targetHookReplacementCodeTemplate.Length - (codeCaveCodeLoc + ret.Length); //again, target (after the hook) minus next instruction location (the NOPs after the end of our injection)
            Array.Copy(targetHookCaveCodeTemplate, ret, ret.Length);
            Array.Copy(BitConverter.GetBytes(addrOffset), 0, ret, ret.Length - 4, 4);
            Array.Copy(BitConverter.GetBytes(targetHookOffset), 0, ret, 2 + 8 + 4 + 3, 4);
            return ret;
        }

        public void setTorrentAnywherePatch(bool on)
        {
            var existingBytes = ReadBytes(erBase + torrentDisabledCheckOne, 3);
            if (existingBytes.SequenceEqual(torrentCheckOrigBytes) && on)
            {
                WriteBytes(erBase + torrentDisabledCheckOne, torrentCheckPatchBytes);
            }
            else if (existingBytes.SequenceEqual(torrentCheckPatchBytes) && !on)
            {
                WriteBytes(erBase + torrentDisabledCheckOne, torrentCheckOrigBytes);
            }

            existingBytes = ReadBytes(erBase + torrentDisabledCheckTwo, 3);
            if (existingBytes.SequenceEqual(torrentCheckOrigBytes) && on)
            {
                WriteBytes(erBase + torrentDisabledCheckTwo, torrentCheckPatchBytes);
            }
            else if (existingBytes.SequenceEqual(torrentCheckPatchBytes) && !on)
            {
                WriteBytes(erBase + torrentDisabledCheckTwo, torrentCheckOrigBytes);
            }
        }

        public void setSoundView(bool on)
        {//this can be enabled in a few ways. either look over all targeting system instances and set a flag, or just patch the location that checks the flag. let's go with the patch.
            //this will also attempt to draw text, which is broken in the game, so the font draw patch must be set
            if (on) { setFontDraw(); }

            var existingBytes = ReadBytes(erBase + soundDrawPatchLoc, 2);
            if (existingBytes.SequenceEqual(soundDrawOrigBytes) && on)
            {
                WriteBytes(erBase + soundDrawPatchLoc, soundDrawPatchBytes);
            }
            else if (existingBytes.SequenceEqual(soundDrawPatchBytes) && !on)
            {
                WriteBytes(erBase + soundDrawPatchLoc, soundDrawOrigBytes);
            }
        }

        public float getSetGameSpeed(float? val = null)
        {
            var ptr = erBase + csFlipperOff;
            var ptr2 = (IntPtr)ReadUInt64(ptr) + gameSpeedOffset;
            var ret = ReadFloat(ptr2);
            if (val.HasValue)
            {
                WriteFloat(ptr2, val.Value);
            }
            return ret;
        }

        public float getSetFrameTimeTarget(float? val = null)
        {//target frame time in seconds, default 1/60.0f
            var ptr = erBase + frameTimeTargetOffset;
            var ret = ReadFloat(ptr);
            if (val.HasValue)
            {
                WriteFloat(ptr, val.Value);
            }
            return ret;
        }

        byte[] getWarpCodeTemplate()
        {
            var buf = warpCodeTemplate.ToArray();
            int callOneAddr = warpFirstCallOffset - zeroCaveOffset - 0x2F;
            Array.Copy(BitConverter.GetBytes(callOneAddr), 0, buf, 4 + 4 + 5 + 5 + 6 + 6 + 7 + 5 + 1, 4);
            int callTwoAddr = warpSecondCallOffset - zeroCaveOffset - 0x39;
            Array.Copy(BitConverter.GetBytes(callTwoAddr), 0, buf, 4 + 4 + 5 + 5 + 6 + 6 + 7 + 5 + 5 + 5 + 1, 4);
            return buf;
        }

        public void doWarp(byte aa, byte bb, byte cc, byte dd)
        {
            var buf = getWarpCodeTemplate();
            buf[9] = aa;
            buf[14] = bb;
            buf[20] = cc;
            buf[26] = dd;
            WriteBytes(erBase + zeroCaveOffset, buf);
            RunThread(erBase + zeroCaveOffset + 4);
            //WriteBytes(erBase + zeroCaveOffset, new byte[buf.Length]); //blank it out - is this really needed?
        }

        public void doWarp(uint mapID)
        {
            uint P3 = (mapID & 0xFF000000U) >> 24; //area number
            uint P2 = (mapID & 0x00FF0000U) >> 16; //X grid
            uint P1 = (mapID & 0x0000FF00U) >> 8; //Z grid
            uint P0 = (mapID & 0x000000FFU); //map type?? this is possibly two nibbles
            doWarp((byte)P3, (byte)P2, (byte)P1, (byte)P0);
        }

        byte[] getItemSpawnTemplate()
        {
            var buf = itemSpawnTemplate.ToArray();
            var mapItemManAddr = (erBase + mapItemManOff).ToInt64();
            Array.Copy(BitConverter.GetBytes(mapItemManAddr), 0, buf, 0x14, 8);
            int callAddr = itemSpawnCall - (itemSpawnStart + 0x24);
            Array.Copy(BitConverter.GetBytes(callAddr), 0, buf, 0x20, 4);
            return buf;
        }

        public void spawnItem(uint itemID, uint qty = 1, uint ashOfWar = 0xFFFFFFFF)
        {
            var buf = getItemSpawnTemplate();
            WriteBytes(erBase + itemSpawnStart, buf);
            WriteUInt32(erBase + itemSpawnData + 0x20, 1); //struct count
            WriteUInt32(erBase + itemSpawnData + 0x24, itemID);
            WriteUInt32(erBase + itemSpawnData + 0x28, qty);
            WriteUInt32(erBase + itemSpawnData + 0x2C, 0); //unused?
            WriteUInt32(erBase + itemSpawnData + 0x30, ashOfWar);
            RunThread(erBase + itemSpawnStart);

            if ((itemID & 0xF0000000) == 0x40000000)
            {//Goods item
                var goodsID = itemID & 0xFFFFFFF;
                var evt = GoodsEvents.getEvent(goodsID);
                if (evt >= 0)
                {
                    Console.WriteLine($"Enabling flag {evt} for goods item {goodsID}");
                    getSetEventFlag(evt, true);
                }
            }
        }

        void openMenuByAddr(int menuAddr)
        {//give menu calls scratch space to write result pointer/code
            RunThread(erBase + menuAddr, param: erBase + zeroCaveOffset);
        }

        public readonly string[] MENUS = { "Credits", "Great Rune", "Mix Physick", "Ashes of War", "Send player home", "Memorise Spell", "Level Up", "Sort Chest", "Rebirth" };

        public void openMenuByName(string name)
        {
            for (int i = 0; i < MENUS.Length; i++)
            {
                if (MENUS[i] == name && i < menuOffsets.Count) { openMenuByAddr(menuOffsets[i]); break; }
            }
        }

        public void setEnemyRepeatActionPatch(bool on)
        {
            var b = ReadUInt8(erBase + enemyRepeatActionOff);
            if (on && b == enemyRepeatActionOrigVal)
            {
                WriteUInt8(erBase + enemyRepeatActionOff, enemyRepeatActionPatchVal);
            }
            else if (!on && b == enemyRepeatActionPatchVal)
            {
                WriteUInt8(erBase + enemyRepeatActionOff, enemyRepeatActionOrigVal);
            }
            else if (on && b == enemyRepeatActionOrigVal108)
            {
                WriteUInt8(erBase + enemyRepeatActionOff, enemyRepeatActionPatchVal108);
            }
            else if (!on && b == enemyRepeatActionPatchVal108)
            {
                WriteUInt8(erBase + enemyRepeatActionOff, enemyRepeatActionOrigVal108);
            }
            else
            {
                Utils.debugWrite("Unexpected value trying to apply enemy repeat action patch");
            }
        }

        byte[] mapOpenInCombatOrig = null;
        byte[] mapStayOpenInCombatOrig = null;

        public void doCombatMapPatch()
        {
            if (ReadUInt8(erBase + mapOpenInCombatOff) == 0xE8)
            {
                mapOpenInCombatOrig = ReadBytes(erBase + mapOpenInCombatOff, mapCombatCheckPatchCode.Length);
                WriteBytes(erBase + mapOpenInCombatOff, mapCombatCheckPatchCode);
            }
            if (ReadUInt8(erBase + mapStayOpenInCombatOff) == 0xE8)
            {
                mapStayOpenInCombatOrig = ReadBytes(erBase + mapStayOpenInCombatOff, mapCombatCheckPatchCode.Length);
                WriteBytes(erBase + mapStayOpenInCombatOff, mapCombatCheckPatchCode);
            }
        }
        public void undoCombatMapPatch()
        {
            if (ReadUInt8(erBase + mapOpenInCombatOff) == 0x31 && mapOpenInCombatOrig != null)
            {
                WriteBytes(erBase + mapOpenInCombatOff, mapOpenInCombatOrig);
            }
            if (ReadUInt8(erBase + mapStayOpenInCombatOff) == 0x31 && mapStayOpenInCombatOrig != null)
            {
                WriteBytes(erBase + mapStayOpenInCombatOff, mapStayOpenInCombatOrig);
            }
        }

        byte[] freeCamPatchAltOrig = null;

        void doFreeCamPatch(bool on = true)
        {
            if (on)
            {
                /*if (!ReadBytes(erBase + freeCamPatchLoc, 2).SequenceEqual(freeCamPatchCode))
                {
                    WriteBytes(erBase + freeCamPatchLoc, freeCamPatchCode);
                }*/
                if (ReadUInt8(erBase + freeCamPatchLocAlt) == 0xEB) //jmp
                {
                    freeCamPatchAltOrig = ReadBytes(erBase + freeCamPatchLocAlt, freeCamPatchCodeAlt.Length);
                    WriteBytes(erBase + freeCamPatchLocAlt, freeCamPatchCodeAlt);
                }
            }
            else
            {
                if (ReadBytes(erBase + freeCamPatchLocAlt, freeCamPatchCodeAlt.Length).SequenceEqual(freeCamPatchCodeAlt))
                {
                    if (freeCamPatchAltOrig != null)
                    {
                        WriteBytes(erBase + freeCamPatchLocAlt, freeCamPatchAltOrig);
                    }
                }
            }
        }

        public void doFreeCamPlayerControlPatch()
        {
            if (ReadBytes(erBase + freeCamPlayerControlPatchLoc, 6).SequenceEqual(freeCamPlayerControlPatchOrig))
            {
                WriteBytes(erBase + freeCamPlayerControlPatchLoc, freeCamPlayerControlPatchReplacement);
            }
            if (ReadBytes(erBase + freeCamPlayerControlPatchLoc, 6).SequenceEqual(freeCamPlayerControlPatchOrig112))
            {
                WriteBytes(erBase + freeCamPlayerControlPatchLoc, freeCamPlayerControlPatchReplacement112);
            }
        }

        public void undoFreeCamPlayerControlPatch()
        {
            if (ReadBytes(erBase + freeCamPlayerControlPatchLoc, 6).SequenceEqual(freeCamPlayerControlPatchReplacement))
            {
                WriteBytes(erBase + freeCamPlayerControlPatchLoc, freeCamPlayerControlPatchOrig);
            }
            if (ReadBytes(erBase + freeCamPlayerControlPatchLoc, 6).SequenceEqual(freeCamPlayerControlPatchReplacement112))
            {
                WriteBytes(erBase + freeCamPlayerControlPatchLoc, freeCamPlayerControlPatchOrig112);
            }
        }

        void setFontDraw()
        {//needed for poise bars and some other things. no need to turn off.
            int oldVal = ReadUInt8(erBase + fontDrawOffset);
            if (oldVal == fontDrawNewValue) { return; }
            WriteUInt8(erBase + fontDrawOffset, fontDrawNewValue);
        }

        void doEventPatch()
        {//not sure what these do, but needed for event draw. thanks pav!
            if (!ReadBytes(erBase + EventPatchLoc1, 2).SequenceEqual(eventPatch))
            {
                WriteBytes(erBase + EventPatchLoc1, eventPatch);
            }
            if (!ReadBytes(erBase + EventPatchLoc2, 2).SequenceEqual(eventPatch))
            {
                WriteBytes(erBase + EventPatchLoc2, eventPatch);
            }
        }

        public void doMusicMutePatch(bool on)
        {
            if (on)
            {
                if (ReadBytes(erBase + musicMuteLoc, 4).SequenceEqual(musicMuteOrig))
                {
                    WriteBytes(erBase + musicMuteLoc, musicMutePatch);
                }
            }
            else
            {
                if (ReadBytes(erBase + musicMuteLoc, 4).SequenceEqual(musicMutePatch))
                {
                    WriteBytes(erBase + musicMuteLoc, musicMuteOrig);
                }
            }
        }

        public bool patchLogos()
        {//see https://github.com/bladecoding/DarkSouls3RemoveIntroScreens/blob/master/SoulsSkipIntroScreen/dllmain.cpp, or my fork i guess
            if (logoScreenBase < 0) { return false; }
            var code = ReadBytes(erBase + logoScreenBase, logoScreenOrig.Length);
            if (code.SequenceEqual(logoScreenOrig))
            {//original code
                WriteBytes(erBase + logoScreenBase, logoScreenPatch);
                Utils.debugWrite("Patched");
                return true;
            }
            else if (code.SequenceEqual(logoScreenPatch))
            {
                Utils.debugWrite("Already patched");
                return true;
            }
            else
            {
                Utils.debugWrite("Unexpected data for logo patch, unknown version?");
                return false;
            }
        }

        public void setFreeUpgrade(bool on)
        {
            WriteUInt8(erBase + upgradeRuneCostOff, (byte)(on ? 0xEB : 0x74)); //patch JE to JMP
            WriteBytes(erBase + upgradeMatCostOff, on ? new byte[] { 0x31, 0xFF } : new byte[] { 0x8B, 0xF8 }); //patch mov edi,eax to xor edi,edi
        }

        public bool installTargetHook()
        {
            //generate code first
            var targetHookReplacementCode = getTargetHookReplacementCode();
            var targetHookCaveCode = getTargetHookCaveCodeTemplate(); //still needs to have ptr addr added in

            var code = ReadBytes(erBase + targetHookLoc, targetHookOrigCode.Length).Take(7); //compare first 7 bytes only; ignores target offset
            if (code.SequenceEqual(targetHookReplacementCode.Take(7)))
            {
                Console.WriteLine("Already hooked");
                return true;
            }
            if (!code.SequenceEqual(targetHookOrigCode.Take(7)))
            {
                Console.WriteLine("Unexpected code at hook location");
                return false;
            }

            var caveCheck1 = ReadUInt64(erBase + codeCavePtrLoc);
            var caveCheck2 = ReadUInt64(erBase + codeCaveCodeLoc);
            if (caveCheck1 != 0x9090909090909090 || caveCheck2 != 0x9090909090909090) //byte reversal doesn't matter
            {
                Console.WriteLine("Code cave not empty");
                return false;
            }

            //set up cave first
            var targetHookFullAddr = erBase + codeCavePtrLoc;
            var caveCode = new byte[targetHookCaveCode.Length];
            Array.Copy(targetHookCaveCode, caveCode, targetHookCaveCode.Length);
            var fullAddrBytes = BitConverter.GetBytes((Int64)targetHookFullAddr);
            Array.Copy(fullAddrBytes, 0, caveCode, 2, 8);
            //patch cave
            WriteBytes(erBase + codeCaveCodeLoc, caveCode);
            //patch hook loc
            WriteBytes(erBase + targetHookLoc, targetHookReplacementCode);
            return true;
        }

        ulong getPlayerInsPtr()
        {
            var ptr1 = ReadUInt64(erBase + worldChrManOff);
            //var ptr2 = ReadUInt64((IntPtr)(ptr1 + worldChrManPlayerOff1));
            //var ptr3 = ReadUInt64((IntPtr)(ptr2 + 0x0));
            //return ptr3;
            var ptr2 = ReadUInt64((IntPtr)(ptr1 + worldChrManPlayerOff2));
            return ptr2;
        }

        ulong getCharPtrModules()
        {
            var ptr3 = getPlayerInsPtr();
            var ptr4 = ReadUInt64((IntPtr)(ptr3 + 0x190)); //set of modules, starting with CS::CSChrDataModule
            return ptr4;
        }

        ulong getCharPtrGameData()
        {
            var ptr3 = getPlayerInsPtr();
            var ptr4 = ReadUInt64((IntPtr)(ptr3 + pgDataOffset)); //CS::PlayerGameData
            return ptr4;
        }

        ulong getTorrentPtr()
        {
            var ptr1 = ReadUInt64(erBase + worldChrManOff);
            //var ptr2 = ReadUInt64((IntPtr)(ptr1 + worldChrManTorrentOff)); //gets a ptr to a ChrSet

            uint torrentID = ReadUInt32((IntPtr)(getCharPtrGameData() + torrentIDOffset));
            if ((torrentID & 0xF0000000) != 0x10000000 || (torrentID & 0x000FFFFF) != 0)
            {
                Utils.debugWrite($"Warning: torrent ID of {torrentID:X8} is unusual"); //normal if you're on the main menu as chr/torrent doesn't exist
            }
            uint chrSetTorrentOff = (torrentID & 0x0FF00000) >> 20;
            var ptr2 = ReadUInt64((IntPtr)(ptr1 + chrSetOffset + chrSetTorrentOff * 8)); //gets a ptr to a ChrSet
            var ptr3 = ReadUInt64((IntPtr)(ptr2 + 0x18)); //no name
            var ptr4 = ReadUInt64((IntPtr)(ptr3)); //CS::EnemyIns
            return ptr4;
        }

        (IntPtr, byte) lookupOpt(DebugOpts opt)
        {//second value is "value for on state" if it's just one byte.
            (IntPtr, byte) badVal = (IntPtr.Zero, 0);
            switch (opt)
            {
                case DebugOpts.COL_MESH_A: return (erBase + meshesOff, 1);
                case DebugOpts.COL_MESH_B: return (erBase + meshesOff + 1, 1);
                case DebugOpts.CHARACTER_MESH: return (erBase + meshesOff + 3, 1);
                case DebugOpts.HITBOX_VIEW_A:
                case DebugOpts.HITBOX_VIEW_B:
                    {
                        var ptr = ReadUInt64(erBase + hitboxBase);
                        if (ptr < SANE_MINIMUM) { return badVal; }
                        if (opt == DebugOpts.HITBOX_VIEW_A) { ptr += hitboxOffset; }
                        else { ptr += hitboxOffset + 1; }
                        return ((IntPtr)ptr, 1);
                    }
                case DebugOpts.DISABLE_MOST_RENDER: return (erBase + groupMaskBase, 0); //TODO: re-check offsets for other patches (especially 1.05)
                case DebugOpts.DISABLE_MAP: return (erBase + groupMaskBase + 1, 0);
                case DebugOpts.DISABLE_TREES: return (erBase + groupMaskTrees, 0);
                case DebugOpts.DISABLE_ROCKS: return (erBase + groupMaskTrees + 1, 0);
                case DebugOpts.DISABLE_DISTANT_MAP: return (erBase + groupMaskTrees + 2, 0);
                case DebugOpts.DISABLE_CHARACTER: return (erBase + groupMaskTrees + 4, 0);
                case DebugOpts.DISABLE_GRASS: return (erBase + groupMaskTrees + 8, 0);

                case DebugOpts.NO_DEATH:
                    {
                        var ptr4 = getCharPtrModules();
                        var ptr5 = ReadUInt64((IntPtr)(ptr4 + 0)); //CS::CSChrDataModule
                        var ptr6 = (IntPtr)(ptr5 + noDeathOffset);
                        return (ptr6, 0x10); //bitfield, bit 0
                    }
                case DebugOpts.ALL_CHR_NO_DEATH:
                    {
                        return (erBase + allChrNoDeath, 1);
                    }
                case DebugOpts.ONE_HP:
                case DebugOpts.MAX_HP:
                    {
                        var ptr4 = getCharPtrModules();
                        var ptr5 = ReadUInt64((IntPtr)(ptr4 + 0)); //CS::CSChrDataModule
                        var ptr6 = (IntPtr)(ptr5 + 0x138);
                        if (opt == DebugOpts.MAX_HP) { return (ptr6, 0xfe); }
                        return (ptr6, 0xff);
                    }
                case DebugOpts.RUNE_ARC:
                    {
                        //var ptrAlternate = getPlayerGameDataPtr();
                        var ptr = getCharPtrGameData();
                        return ((IntPtr)ptr + 0xFF, 1);
                    }
                case DebugOpts.INSTANT_QUITOUT:
                    {
                        var ptr = ReadUInt64(erBase + quitoutBase);
                        return ((IntPtr)(ptr + 0x10), 1);
                    }
                case DebugOpts.DISABLE_AI:
                    {
                        return (erBase + noAiUpdate, 1);
                    }
                case DebugOpts.NO_GOODS:
                    {
                        return (erBase + noGoodsConsume, 1);
                    }
                case DebugOpts.NO_STAM:
                    {
                        return (erBase + noGoodsConsume + 1, 1);
                    }
                case DebugOpts.NO_FP:
                    {
                        return (erBase + noGoodsConsume + 2, 1);
                    }
                case DebugOpts.NO_ARROWS:
                    {
                        return (erBase + noGoodsConsume + 3, 1);
                    }
                case DebugOpts.ONE_SHOT:
                    {
                        return (erBase + noGoodsConsume - 1, 1);
                    }
                case DebugOpts.NO_GRAVITY_ALTERNATE: //not currently used
                    {//this is the "another no gravity" pointer. there is another flag available (the not-another one)
                     //this one makes the player float up slightly. it blocks teleport
                     //the other one does not cause a float but allows teleport. hmmm.
                        var ptr2 = getPlayerInsPtr();
                        return ((IntPtr)(ptr2 + 0x1C4), 0x15);
                    }
                case DebugOpts.NO_GRAVITY:
                    {
                        var ptr3 = getCharPtrModules();
                        var ptr4 = ReadUInt64((IntPtr)(ptr3 + 0x68)); //CS::CSChrPhysicsModule
                        return ((IntPtr)(ptr4 + 0x1D3), 1);
                    }
                case DebugOpts.NO_MAP_COLLISION:
                    {
                        var ptr2 = getPlayerInsPtr();
                        var ptr3 = ReadUInt64((IntPtr)(ptr2 + 0x58));
                        return ((IntPtr)(ptr3 + 0xF0), 0x13);
                    }
                case DebugOpts.TOP_DEBUG_MENU:
                    {//gone in recent patches. Sadge.
                        var ptr = ReadUInt64(erBase + newMenuSystem);
                        return ((IntPtr)(ptr + 0x891), 1);
                    }
                case DebugOpts.POISE_VIEW:
                    {
                        var ptr = ReadUInt64(erBase + chrDbg);
                        return ((IntPtr)(ptr + 0x69), 1);
                    }
                case DebugOpts.TORRENT_NO_DEATH:
                    {
                        var ptr1 = getTorrentPtr();
                        var ptr2 = ReadUInt64((IntPtr)(ptr1 + 0x190));
                        var ptr3 = ReadUInt64((IntPtr)(ptr2 + 0)); //CS::CSChrDataModule
                        return ((IntPtr)(ptr3 + noDeathOffset), 0x10); //same offset as player no death
                    }
                case DebugOpts.TORRENT_NO_GRAV_ALT: //not currently used
                    {//this is the 'another no grav' flag, equivalent to the player version.
                        var ptr1 = getTorrentPtr();
                        return ((IntPtr)(ptr1 + 0x1C4), 0x15);
                    }
                case DebugOpts.TORRENT_NO_GRAV:
                    {
                        var ptr1 = getTorrentPtr();
                        var ptr2 = ReadUInt64((IntPtr)(ptr1 + 0x190));
                        var ptr3 = ReadUInt64((IntPtr)(ptr2 + 0x68)); //CS::CSChrPhysicsModule
                        return ((IntPtr)(ptr3 + 0x1D3), 1);
                    }
                case DebugOpts.TORRENT_NO_MAP_COLL:
                    {
                        var ptr1 = getTorrentPtr();
                        var ptr2 = ReadUInt64((IntPtr)(ptr1 + 0x58));
                        return ((IntPtr)(ptr2 + 0xF0), 0x13);
                    }
                case DebugOpts.EVENT_DRAW:
                    {
                        var ptr1 = ReadUInt64(erBase + DbgEventManOff);
                        return ((IntPtr)(ptr1 + 0x4), 1);
                    }
                case DebugOpts.EVENT_STOP:
                    {
                        var ptr1 = ReadUInt64(erBase + DbgEventManOff);
                        return ((IntPtr)(ptr1 + 0x28), 1);
                    }
                case DebugOpts.FREE_CAM:
                    {
                        var ptr1 = ReadUInt64(erBase + FieldAreaOff);
                        var ptr2 = ReadUInt64((IntPtr)(ptr1 + 0x20)); //CS::GameRend
                        return ((IntPtr)(ptr2 + 0xC8), 1);
                    }
                case DebugOpts.DISABLE_STEAM_INPUT_ENUM:
                    {
                        var ptr = ReadUInt64(erBase + usrInputMgrImplOff);
                        return ((IntPtr)(ptr + usrInputMgrImpSteamInputFlagOff), 1);
                    }
                case DebugOpts.DISABLE_STEAM_ACHIVEMENTS:
                    {
                        var ptr = ReadUInt64(erBase + trophyImpOffset);
                        var ptr2 = ReadUInt64((IntPtr)ptr + 8);
                        return ((IntPtr)ptr2 + 0x4c, 0); //not sure if offset is patch stable, but it's likely as it's a fairly low offset
                    }
                case DebugOpts.TARGETING_VIEW:
                    {
                        var ptr = erBase + allTargetingDebugDraw;
                        return (ptr, 1);
                    }
            }
            return badVal;
        }

        public void cycleMeshColours()
        {
            IntPtr addr = erBase + meshesOff + 8;
            int meshColours = ReadInt32(addr);
            meshColours++;
            if (meshColours > 3) { meshColours = 0; }
            WriteInt32(addr, meshColours);
        }

        int? targetHpFreeze = null;

        public void enableOpt(DebugOpts opt)
        {
            if (opt == DebugOpts.TARGET_HP)
            {//special case
                getSetTargetInfo(TargetInfo.HP, targetHpFreeze);
                return;
            }

            if (opt == DebugOpts.POISE_VIEW)
            {//special case - must alter this value or crash
                setFontDraw();
            }

            if (opt == DebugOpts.EVENT_DRAW)
            {
                setFontDraw(); //not strictly necessary but seems to reduce the crash risk
                doEventPatch();
            }

            if (opt == DebugOpts.FREE_CAM)
            {
                doFreeCamPatch();
            }

            var tuple = lookupOpt(opt);
            if (tuple.Item1 == IntPtr.Zero) { Utils.debugWrite("Can't enable " + opt); return; }

            if ((long)tuple.Item1 < SANE_MINIMUM || (long)tuple.Item1 > SANE_MAXIMUM) { return; }

            var val = tuple.Item2;
            if (val == 0 || val == 1)
            {
                WriteUInt8(tuple.Item1, val);
            }
            else if (val >= 0x10 && val <= 0x17)
            {//bitfield (set to enable)
                int setMask = 1 << (val - 0x10);
                var oldVal = ReadUInt8(tuple.Item1);
                var newVal = oldVal | setMask;
                WriteUInt8(tuple.Item1, (byte)newVal);
            }
            else if (val == 0xff)
            {//special case, write 1 as 32 bit
                WriteUInt32(tuple.Item1, 1);
            }
            else if (val == 0xfe)
            {//special case, read next 32 bit int and write
                var nextVal = ReadUInt32(tuple.Item1 + 4); //max HP is after hp in the character struct
                WriteUInt32(tuple.Item1, nextVal);
            }
        }
        public void disableOpt(DebugOpts opt)
        {
            if (opt == DebugOpts.FREE_CAM)
            {//special case - fix kb&m breaking on quit to main menu
                doFreeCamPatch(false);
            }

            var tuple = lookupOpt(opt);
            if (tuple.Item1 == IntPtr.Zero) { return; }

            if ((long)tuple.Item1 < SANE_MINIMUM || (long)tuple.Item1 > SANE_MAXIMUM) { return; }

            var val = tuple.Item2;
            if (val == 0 || val == 1)
            {
                var newVal = (tuple.Item2 == 1) ? (byte)0 : (byte)1;
                WriteUInt8(tuple.Item1, newVal);
            }
            else if (val >= 0x10 && val <= 0x17)
            {//bitfield (clear to disable)
                int setMask = 1 << (val - 0x10);
                var oldVal = ReadUInt8(tuple.Item1);
                var newVal = oldVal & ~setMask;
                WriteUInt8(tuple.Item1, (byte)newVal);
            }
            else if (val == 0xff)
            {//special case, write 9999 as 32 bit (could load max hp but this is clamped by the game anyway)
                WriteUInt32(tuple.Item1, 9999);
            }
            else if (val == 0xfe)
            {//nothing to do
            }
        }

        object setLock = new object();

        public void freezeOn(DebugOpts opt)
        {
            if (opt == DebugOpts.TARGET_HP)
            {//special case
                targetHpFreeze = (int)getSetTargetInfo(TargetInfo.HP);
            }

            lock (setLock)
            {
                freezeSet.Add(opt);
            }
        }
        public void offAndUnFreeze(DebugOpts opt)
        {
            lock (setLock)
            {
                unFreezeSet.Add(opt);
            }
        }

        HashSet<DebugOpts> freezeSet = new HashSet<DebugOpts>();
        HashSet<DebugOpts> unFreezeSet = new HashSet<DebugOpts>();

        public bool weGood { get; set; } = true;

        void freezeFunc()
        {//CE style "freeze" that repeatedly sets the value
            while (_running)
            {
                weGood = ReadTest(erBase); //is it possible to come good later, or should we just fail immediately and dispose ourself?
                lock (setLock)
                {
                    foreach (var opt in unFreezeSet)
                    {
                        disableOpt(opt);
                        if (freezeSet.Contains(opt)) { freezeSet.Remove(opt); }
                    }
                    unFreezeSet.Clear();
                    foreach (var opt in freezeSet)
                    {
                        enableOpt(opt);
                    }
                }
                Thread.Sleep(100); //arbitrary. will except here when closing program, but nothing needs to be done.
            }
        }

        public float getTargetDefenses(TargetInfo stat)
        {
            var targetPtr = (IntPtr)ReadUInt64(erBase + codeCavePtrLoc);
            var ptr1 = (IntPtr)ReadUInt64(targetPtr + 0x58);
            var ptr2 = (IntPtr)ReadUInt64(ptr1 + 0x18);
            var ptr3 = (IntPtr)ReadUInt64(ptr2 + 0xC0);
            var npcParamPtr = (IntPtr)ReadUInt64(ptr3 + 0x18); // 

            switch (stat)
            {
                case TargetInfo.STANDARD:
                    return ReadFloat(npcParamPtr + 0x1A4);
                case TargetInfo.SLASH:
                    return ReadFloat(npcParamPtr + 0x1A8);
                case TargetInfo.STRIKE:
                    return ReadFloat(npcParamPtr + 0x1AC);
                case TargetInfo.PIERCE:
                    return ReadFloat(npcParamPtr + 0x1B0);
                case TargetInfo.MAGIC:
                    return ReadFloat(npcParamPtr + 0x1B4);
                case TargetInfo.FIRE:
                    return ReadFloat(npcParamPtr + 0x1B8);
                case TargetInfo.LIGHTNING:
                    return ReadFloat(npcParamPtr + 0x1BC);
                case TargetInfo.HOLY:
                    return ReadFloat(npcParamPtr + 0x1C0);
                default:
                    return 0.0f;
            }

        }

        public double getSetTargetInfo(TargetInfo info, int? setVal = null)
        {//most are actually ints but it's easier just to use a common type. double can store fairly large ints exactly.
            double ret = double.NaN;
            var targetPtr = ReadUInt64(erBase + codeCavePtrLoc); //CS::EnemyIns
            if (targetPtr < SANE_MINIMUM || targetPtr > SANE_MAXIMUM) { return ret; }
            var p1 = ReadUInt64((IntPtr)(targetPtr + 0x190)); //modules offset?
            if (p1 < SANE_MINIMUM || p1 > SANE_MAXIMUM) { return ret; }

            uint p2off = 0;
            switch (info)
            {
                case TargetInfo.HP:
                case TargetInfo.MAX_HP:
                    break; //CS::CSChrDataModule
                case TargetInfo.POISE:
                case TargetInfo.MAX_POISE:
                case TargetInfo.POISE_TIMER:
                    p2off = 0x40; //CS::CSEnemySuperArmorModule
                    break;
                default: //assume resists.
                    p2off = 0x20; //CS::CSChrResistModule
                    break;
            }
            var p2 = ReadUInt64((IntPtr)(p1 + p2off));

            uint p3off = 0;
            switch (info)
            {
                case TargetInfo.HP:
                    p3off = 0x138; break;
                case TargetInfo.MAX_HP:
                    p3off = 0x13c; break;
                case TargetInfo.POISE:
                    p3off = 0x10; break;
                case TargetInfo.MAX_POISE:
                    p3off = 0x14; break;
                case TargetInfo.POISE_TIMER:
                    p3off = 0x1c; break;
                default:
                    {//assume resists
                        int poisonOff = info - TargetInfo.POISON;
                        int statIndex = poisonOff / 2;
                        bool isMax = (poisonOff % 2) == 1;
                        p3off = (uint)((isMax ? 0x2c : 0x10) + 4 * statIndex);
                        break;
                    }
            }
            var pFinal = (IntPtr)(p2 + p3off);

            var fourBytes = ReadBytes(pFinal, 4);

            if (setVal.HasValue)
            {//TODO: support setting float? pass in object i guess
                WriteInt32(pFinal, setVal.Value);
                if (info == TargetInfo.HP)
                {//special case
                    targetHpFreeze = (int)setVal; //change our freeze value. the freeze itself will keep setting this, but that's harmless.
                }
                return (double)BitConverter.ToInt32(fourBytes, 0); //no real need to return anything tbh
            }

            switch (info)
            {
                case TargetInfo.POISE:
                case TargetInfo.MAX_POISE:
                case TargetInfo.POISE_TIMER:
                    {
                        return (double)BitConverter.ToSingle(fourBytes, 0);
                    }
            }

            return (double)BitConverter.ToInt32(fourBytes, 0);
        }

        public int getSetPlayerHP(int? val = null)
        {
            var ptr4 = getCharPtrModules();
            var ptr5 = ReadUInt64((IntPtr)(ptr4 + 0)); //CS::CSChrDataModule
            var ptr6 = (IntPtr)(ptr5 + 0x138);
            int ret = ReadInt32(ptr6);
            if (val.HasValue) { WriteInt32(ptr6, val.Value); }
            return ret;
        }

        int statsOffset = 0x3c; //possible but unlikely to change between patches. TODO: scan?
        int levelOffset = 0x68; //same
        public readonly string[] STAT_NAMES = new string[] { "Vigor", "Mind", "Endurance", "Strength", "Dexterity", "Intelligence", "Faith", "Arcane" };
        public readonly string[] DLC_STAT_NAMES = new string[] { "Scadu", "Ash" };
        public List<(string, int)> getSetPlayerStats(List<(string, int)> newStats = null)
        {
            var ptr = (IntPtr)getCharPtrGameData();
            var ret = new List<(string, int)>();

            int newLevel = -79; //+ 10 * 8 stats = RL1
            int i = 0;
            for (; i < STAT_NAMES.Length; i++)
            {
                int statOffset = statsOffset + i * 4;
                int currentVal = ReadInt32(ptr + statOffset);
                if (newStats != null) { WriteInt32(ptr + statOffset, newStats[i].Item2); newLevel += newStats[i].Item2; }
                ret.Add((STAT_NAMES[i], currentVal));
            }
            if (newStats != null)
            {
                WriteInt32(ptr + levelOffset, newLevel);
            }

            if (exeSupportsDlc())
            {//could be simplified i guess, except these stats are one byte for some reason
                for (int j = 0; j < DLC_STAT_NAMES.Length; j++)
                {
                    int statOffset = scadOffset + j;
                    var currentVal = ReadUInt8(ptr + statOffset);
                    if (newStats != null) { WriteUInt8(ptr + statOffset, (byte)newStats[i + j].Item2); }
                    ret.Add((DLC_STAT_NAMES[j], currentVal));
                }
            }
            return ret;
        }

        public int getSetClearCount(int? newVal = null)
        {
            var ptr = (IntPtr)ReadUInt64(erBase + gameDataMan);
            int oldVal = ReadInt32(ptr + 0x120); //doesn't change between patches
            if (newVal.HasValue) { WriteInt32(ptr + 0x120, newVal.Value); }
            return oldVal;
        }

        public void triggerNGPlus()
        {
            var ptr = (IntPtr)ReadUInt64(erBase + quitoutBase);
            WriteUInt8(ptr + triggerNGPlusOffset, 1);
        }

        public (float, float, float) getSetPlayerLocalCoords((float, float, float)? pos = null)
        {
            var ptr4 = getCharPtrModules();
            var ptr5 = ReadUInt64((IntPtr)(ptr4 + 0x68)); //CS::CSChrPhysicsModule
            var ptrX = (IntPtr)(ptr5 + 0x70);
            var ptrY = (IntPtr)(ptr5 + 0x74);
            var ptrZ = (IntPtr)(ptr5 + 0x78);

            float x = ReadFloat(ptrX);
            float y = ReadFloat(ptrY);
            float z = ReadFloat(ptrZ);

            if (pos != null)
            {
                WriteFloat(ptrX, pos.Value.Item1);
                WriteFloat(ptrY, pos.Value.Item2);
                WriteFloat(ptrZ, pos.Value.Item3);
            }

            return (x, y, z);
        }

        public (float, float, float) getSetTorrentLocalCoords((float, float, float)? pos = null)
        {
            var ptr1 = getTorrentPtr();
            var ptr2 = ReadUInt64((IntPtr)(ptr1 + 0x190));
            var ptr3 = ReadUInt64((IntPtr)(ptr2 + 0x68)); //CS::CSChrPhysicsModule
            var ptrX = (IntPtr)(ptr3 + 0x70);
            var ptrY = (IntPtr)(ptr3 + 0x74);
            var ptrZ = (IntPtr)(ptr3 + 0x78);

            float x = ReadFloat(ptrX);
            float y = ReadFloat(ptrY);
            float z = ReadFloat(ptrZ);

            if (pos != null)
            {
                WriteFloat(ptrX, pos.Value.Item1);
                WriteFloat(ptrY, pos.Value.Item2);
                WriteFloat(ptrZ, pos.Value.Item3);
            }

            return (x, y, z);
        }

        public (float, float, float) getSetLocalCoords((float, float, float)? pos = null)
        {
            if (isRiding()) { return getSetTorrentLocalCoords(pos); }
            return getSetPlayerLocalCoords(pos);
        }

        public (float, float, float, float, uint) getMapCoords()
        {//aka "chunk coords", sometimes called 'global coords' though the x/y/z is not global
            var ptr2 = getPlayerInsPtr();
            float mx = ReadFloat((IntPtr)(ptr2 + mapIDinPlayerIns - 16));
            float my = ReadFloat((IntPtr)(ptr2 + mapIDinPlayerIns - 12));
            float mz = ReadFloat((IntPtr)(ptr2 + mapIDinPlayerIns - 8));
            float mRad = ReadFloat((IntPtr)(ptr2 + mapIDinPlayerIns - 4)); //character facing direction. not camera. not needed to port but may be useful.
            uint mapID = ReadUInt32((IntPtr)(ptr2 + mapIDinPlayerIns));

            //rad of +/- Pi, North, +Z
            //rad of -Pi/2, East, +X
            //rad of 0, South, -Z
            //rad of Pi/2, West, -X
            return (mx, my, mz, mRad, mapID);
        }

        public int teleportToGlobal((float, float, float, float, uint) targetCoords, float yOffset = 0, bool justConvPos = false, bool warpIfNeeded = false)
        {//this is imperfect, but at least it works within the main world pretty well.
            if (!isGameLoaded()) { Console.WriteLine("Game not loaded, not teleporting"); return -1; }
            var currentMapCoords = getMapCoords();
            var worldIDCur = (currentMapCoords.Item5 & 0xFF000000) >> 24;
            var worldIDTarget = (targetCoords.Item5 & 0xFF000000) >> 24;
            if (targetCoords.Item5 == currentMapCoords.Item5)
            {//easy case, exactly the same map section
                Utils.debugWrite("Teleport is within same map section");
                var xChg = targetCoords.Item1 - currentMapCoords.Item1;
                var yChg = targetCoords.Item2 - currentMapCoords.Item2 + yOffset;
                var zChg = targetCoords.Item3 - currentMapCoords.Item3;
                var localCoords = getSetLocalCoords();
                var localCoordsNew = (localCoords.Item1 + xChg, localCoords.Item2 + yChg, localCoords.Item3 + zChg);
                getSetLocalCoords(localCoordsNew);
            }
            else if (warpIfNeeded && !justConvPos && worldIDCur != worldIDTarget)
            {//TODO: more extensive conditions for 'warp needed', may help in areas like siofra.
                Utils.debugWrite("World ID differs, will warp first");
                doWarp(targetCoords.Item5);
                Thread.Sleep(1000);
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(500);
                    if (!isGameLoaded()) { Console.Write("."); continue; }
                    currentMapCoords = getMapCoords();
                    worldIDCur = (currentMapCoords.Item5 & 0xFF000000) >> 24;
                    if (worldIDCur == worldIDTarget)
                    {//you don't always end up in the exact same map segment. matching world id is close enough.
                        Utils.debugWrite("Warp done, teleporting to actual location");
                        teleportToGlobal(targetCoords, yOffset, warpIfNeeded: false);
                        return 1; //warped
                    }
                    Console.Write(",");
                }
                //it is unclear why but warping to certain map IDs just does not work
                if (currentMapCoords.Item5 == 0xFFFFFFFFU)
                {
                    Utils.debugWrite("Bad map after warp, warping to roundtable");
                    doWarp(185204736);
                    Thread.Sleep(1000);
                    for (int i = 0; i < 30; i++)
                    {
                        Thread.Sleep(500);
                        if (!isGameLoaded()) { Console.Write("."); continue; }
                        break;
                    }
                    return -1;
                }
                Utils.debugWrite("Loading timed out"); //15 sec. TODO: better scheme than simple timeout?
                return -1; //failed
            }
            else
            {
                Utils.debugWrite("Using global coords conversion");
                var globalCoordsNow = TeleportHelper.getWorldMapCoords(currentMapCoords);
                var globalCoordsTarget = TeleportHelper.getWorldMapCoords(targetCoords, justConvPos);
                if (float.IsNaN(globalCoordsNow.Item1) || float.IsNaN(globalCoordsTarget.Item1)) //check if just X is NaN - either indicates we can't do it
                {
                    Utils.debugWrite("Unable to teleport");
                    return -1; //failed
                }
                var xChg = globalCoordsTarget.Item1 - globalCoordsNow.Item1;
                var yChg = globalCoordsTarget.Item2 - globalCoordsNow.Item2 + yOffset;
                var zChg = globalCoordsTarget.Item3 - globalCoordsNow.Item3;
                var localCoords = getSetLocalCoords();
                var localCoordsNew = (localCoords.Item1 + xChg, localCoords.Item2 + yChg, localCoords.Item3 + zChg);
                getSetLocalCoords(localCoordsNew);
            }
            return 0; //teleported
        }
        public IntPtr getFreeCamPtr()
        {//pointer to CSDebugCam
            var ptr1 = ReadUInt64(erBase + FieldAreaOff);
            var ptr2 = ReadUInt64((IntPtr)(ptr1 + 0x20));
            var ptr3 = ReadUInt64((IntPtr)(ptr2 + 0xD0));
            return (IntPtr)ptr3;
        }

        public (float, float, float) getSetFreeCamCoords((float, float, float)? pos = null)
        {
            var ptr3 = getFreeCamPtr();
            var ptrX = (IntPtr)(ptr3 + 0x40);
            var ptrY = (IntPtr)(ptr3 + 0x44);
            var ptrZ = (IntPtr)(ptr3 + 0x48);

            float x = ReadFloat(ptrX);
            float y = ReadFloat(ptrY);
            float z = ReadFloat(ptrZ);

            if (pos != null)
            {
                WriteFloat(ptrX, pos.Value.Item1);
                WriteFloat(ptrY, pos.Value.Item2);
                WriteFloat(ptrZ, pos.Value.Item3);
            }

            return (x, y, z);
        }

        const int MAX_RUNES = 999999999;
        const long MAX_RUNE_MEMORY = 0xffffffff;

        public int addRunes(int amount = 1000000)
        {//this should now be functionally equivalent to the AddSouls function in game
            var ptr = (IntPtr)getCharPtrGameData() + 0x6c; //this location is close to player name (9c) and multiplayer group passwords a bit later.
            var ptrReachedMaxRuneMemory = (IntPtr)getCharPtrGameData() + 0x109;
            var oldRunes = ReadInt32(ptr);
            var oldRuneMemory = ReadUInt32(ptr + 4);

            var newRunes = oldRunes + amount;
            if (newRunes < 0) { newRunes = 0; }
            else if (newRunes > MAX_RUNES) { newRunes = MAX_RUNES; }
            WriteInt32(ptr, newRunes); //update it without checking that it increased at all, because the game does so

            var increase = newRunes - oldRunes;
            if (0 == increase) { return increase; } //if there's no increase, then we don't update rune memory

            long newRuneMemory = oldRuneMemory;
            newRuneMemory += increase;
            if (newRuneMemory > MAX_RUNE_MEMORY) { newRuneMemory = MAX_RUNE_MEMORY; } //the game stores this as a 64-bit int, but caps it at the max for a 32 bit uint
            else if (newRuneMemory < 0) { newRuneMemory = newRunes; } //this check doesn't really make sense, but the game does it...
            bool reachedMaxRuneMemory = MAX_RUNE_MEMORY == newRuneMemory;

            WriteUInt32(ptr + 4, (uint)newRuneMemory);
            WriteUInt32(ptr + 8, 0);
            WriteUInt8(ptrReachedMaxRuneMemory, reachedMaxRuneMemory ? (byte)1 : (byte)0);

            return increase;
        }

        public bool isRiding()
        {//there's multiple ways to get a 'riding state'. see pav's table.
            var ptr = getCharPtrModules() + 0xE8; //CS::CSChrRideModule
            var ptr2 = ReadInt64((IntPtr)ptr) + 0x160;
            var rideStatus = ReadUInt32((IntPtr)ptr2);
            return (rideStatus >> 24) == 0x01;
        }

        //thanks nord!
        enum CSFD4VirtualMemoryFlag
        {
            EventFlagDivisor = 0x1C,
            FlagHolderEntrySize = 0x20,
            FlagHolderEntryCount = 0x24,
            FlagHolder = 0x28,
            FlagGroupAllocator = 0x30,
            FlagGroupRootNode = 0x38,
            FlagGroupEntryCount = 0x40
        }
        enum EventFlagGroupNode
        {
            Left = 0x0,
            Parent = 0x8,
            Right = 0x10,
            IsLeaf = 0x19,
            Group = 0x20,
            LocationMode = 0x28,
            Location = 0x30,
        }
        public (IntPtr, int) getEventFlagLocAndBit(int flag)
        {
            var evtFlagMan = (IntPtr)ReadUInt64(erBase + csEventFlagMan);
            int divisor = ReadInt32(evtFlagMan + (int)CSFD4VirtualMemoryFlag.EventFlagDivisor); //in practice this should never change. could just hardcode to 1000
            int entrySize = ReadInt32(evtFlagMan + (int)CSFD4VirtualMemoryFlag.FlagHolderEntrySize); //usually 125?
            if (divisor == 0 || entrySize == 0) { return (IntPtr.Zero, 0); } //game hasn't loaded yet
            int groupNum = flag / divisor;
            int bitNumFull = flag % divisor;

            var root = (IntPtr)ReadUInt64(evtFlagMan + (int)CSFD4VirtualMemoryFlag.FlagGroupRootNode);
            var parent = (IntPtr)ReadUInt64(root + (int)EventFlagGroupNode.Parent);
            var current = parent;
            bool isLeaf = ReadUInt8(current + (int)EventFlagGroupNode.IsLeaf) != 0;

            var found = root;

            int walkCount = 0;
            while (!isLeaf)
            {
                if (++walkCount > 1000) { return (IntPtr.Zero, 0); } //something is wrong
                int currentGroup = ReadInt32(current + (int)EventFlagGroupNode.Group);
                var next = IntPtr.Zero;
                if (currentGroup < groupNum)
                {
                    next = (IntPtr)ReadUInt64(current + (int)EventFlagGroupNode.Right);
                    current = found;
                }
                else
                {
                    next = (IntPtr)ReadUInt64(current + (int)EventFlagGroupNode.Left);
                }

                found = current;
                current = next;
                isLeaf = ReadUInt8(next + (int)EventFlagGroupNode.IsLeaf) != 0;
            }

            if (found == root || groupNum < ReadInt32(found + (int)EventFlagGroupNode.Group))
            {//failure
                return (IntPtr.Zero, 0);
            }
            int locMode = ReadInt32(found + (int)EventFlagGroupNode.LocationMode);
            if (locMode == 2)
            {//does this actually get used?
                var ptr = (IntPtr)ReadUInt64(found + (int)EventFlagGroupNode.Location);
                return (ptr, bitNumFull);
            }
            if (locMode == 1)
            {
                var flagHolder = (IntPtr)ReadUInt64(evtFlagMan + (int)CSFD4VirtualMemoryFlag.FlagHolder);
                int loc = ReadInt32(found + (int)EventFlagGroupNode.Location);
                int locOffset = loc * entrySize;
                var ptr = flagHolder + locOffset;
                return (ptr, bitNumFull);
            }
            //unknown loc mode; failure
            return (IntPtr.Zero, 0);
        }

        public bool getSetEventFlag(int flag, bool? on = null)
        {
            var loc = getEventFlagLocAndBit(flag);
            if (loc.Item1 == IntPtr.Zero) { Console.WriteLine($"Could not find flag {flag}"); return false; }
            var byteNum = loc.Item2 / 8;
            int bitNum = 7 - loc.Item2 % 8; //???
            var flagMask = 1 << bitNum;
            var flagByte = ReadUInt8(loc.Item1 + byteNum);
            var flagState = (flagByte & flagMask) == flagMask;
            if (on.HasValue)
            {
                if (on.Value)
                {
                    flagByte |= (byte)flagMask;
                }
                else
                {
                    flagByte &= (byte)~flagMask;
                }
                WriteUInt8(loc.Item1 + byteNum, flagByte);
            }
            return flagState;
        }

        public bool isGameLoaded()
        {
            var loc = getEventFlagLocAndBit(2200);
            if (loc.Item1 == IntPtr.Zero) { return false; } //flags not loaded
            //if (getSetEventFlag(2200)) { return false; } //loading flag? sometimes not set even when loaded in, though (stranded graveyard)

            var ptr = (IntPtr)getCharPtrGameData(); //is there a nice way to validate a pointer?
            var level = ReadInt32(ptr + levelOffset); //check level
            if (level < 1 || level > 713) { return false; }

            return true;
        }

#if DEBUG
        public void runFlagTests()
        {
            var rand = new Random();
            var sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < 1000; i++)
            {
                var val = getSetEventFlag(i);
                //var val = getSetEventFlag(rand.Next(1000000));
                //if (val) { System.Diagnostics.Debugger.Break(); }
            }
            sw.Stop();
            Console.WriteLine($"kiloflag time {sw.ElapsedMilliseconds} ms"); //~50ms on my machine
        }
#endif
        public void AllocateMem()
        {
            IntPtr searchRangeStart = erBase - 0x40000000;
            IntPtr searchRangeEnd = erBase - 0x30000;
            uint codeCaveSize = 0x2000;
            IntPtr allocatedMemory;

            for (IntPtr addr = searchRangeEnd; addr.ToInt64() > searchRangeStart.ToInt64(); addr -= 0x10000)
            {
                allocatedMemory = VirtualAllocEx(_targetProcessHandle, addr, codeCaveSize);

                if (allocatedMemory != IntPtr.Zero)
                {
                    CodeCaveOffsets.Base = allocatedMemory;
                    break;
                }
            }
        }

        public void ToggleReducedTargetView(bool isEnabled)
        {
            var code = CodeCaveOffsets.Base + (int)CodeCaveOffsets.ReducedTargetView.Code;
            if (isEnabled)
            {
                var maxDist = CodeCaveOffsets.Base + (int)CodeCaveOffsets.ReducedTargetView.MaxDist;
                WriteFloat(maxDist, 100.0f * 100.0f);
                var codeBytes = AsmLoader.GetAsmBytes("ReducedTargetView");
                var bytes = BitConverter.GetBytes(erBase.ToInt64() + worldChrManOff);
                var hook = erBase.ToInt64() + _blueTargetViewHook;
                Array.Copy(bytes, 0, codeBytes, 0x36 + 2, 8);
                AsmHelper.WriteRelativeOffsets(codeBytes, new[]
                {
                    (code.ToInt64() + 0x86, maxDist.ToInt64(), 8, 0x86 + 4 ),
                    (code.ToInt64() + 0xC4, hook + 0x5, 5, 0xC4 + 1),
                    (code.ToInt64() + 0xCA, hook + 0x141, 5, 0xCA + 1),
                });
                WriteBytes(code, codeBytes);
                _hookManager.InstallHook(code.ToInt64(), hook, new byte[]
                    { 0x48, 0x8D, 0x54, 0x24, 0x40 });
            }
            else
            {
                _hookManager.UninstallHook(code.ToInt64());
            }

        }

        public void SetTargetViewMaxDist(float reducedTargetViewDistance)
        {
            var maxDist = CodeCaveOffsets.Base + (int)CodeCaveOffsets.ReducedTargetView.MaxDist;
            WriteFloat(maxDist, reducedTargetViewDistance * reducedTargetViewDistance);
        }

        public void ToggleRykardHook(bool isEnabled)
        {
            var code = CodeCaveOffsets.Base + CodeCaveOffsets.Rykard;
            if (isEnabled)
            {
                var hook = erBase.ToInt64() + _hasSpEffectHook;
                var codeBytes = AsmLoader.GetAsmBytes("RykardNoMega");
                var bytes = AsmHelper.GetJmpOriginOffsetBytes(hook, 7, code + 0x17);
                Array.Copy(bytes, 0, codeBytes, 0x12 + 1, 4);
                WriteBytes(code, codeBytes);
                _hookManager.InstallHook(code.ToInt64(), hook, new byte[]
                    { 0x48, 0x8B, 0x49, 0x08, 0x48, 0x85, 0xC9 });
            }
            else
            {
                _hookManager.UninstallHook(code.ToInt64());
            }
        }

        public void ToggleInfinitePoise(bool isInfinitePoiseEnabled)
        {
            var code = CodeCaveOffsets.Base + CodeCaveOffsets.InfinitePoise;

            if (isInfinitePoiseEnabled)
            {
                var hook = erBase.ToInt64() + _infinitePoiseHook;
                var codeBytes = AsmLoader.GetAsmBytes("InfinitePoise");
                var bytes = BitConverter.GetBytes(erBase.ToInt64() + worldChrManOff);
                Array.Copy(bytes, 0, codeBytes, 0x1 + 2, 8);
                AsmHelper.WriteJumpOffsets(codeBytes, new[]
                {
                    (hook, 7, code + 0x1D, 0x1D + 1),
                    (hook, 7, code + 0x2A, 0x2A + 1),
                });
                WriteBytes(code, codeBytes);
                _hookManager.InstallHook(code.ToInt64(), hook, new byte[]
                    { 0x80, 0xBF, 0x5F, 0x02, 0x00, 0x00, 0x00 });
            }
            else
            {
                _hookManager.UninstallHook(code.ToInt64());
            }
        }

        public void ForceSave() =>
            WriteUInt8((IntPtr)ReadInt64(erBase + quitoutBase) + 0xb72, 1);
    }

    public class TeleportHelper
    {
        public static string mapIDString(uint mapID)
        {
            uint P3 = (mapID & 0xFF000000U) >> 24; //area number
            uint P2 = (mapID & 0x00FF0000U) >> 16; //X grid
            uint P1 = (mapID & 0x0000FF00U) >> 8; //Z grid
            uint P0 = (mapID & 0x000000FFU); //map type?? this is possibly two nibbles
            return $"{P3} {P2} {P1} {P0}";
        }

        public const int MAIN_WORLD_ID = 60;
        public const int DLC_WORLD_ID = 61;
        const float TILE_SIZE = 256;

        public static bool mapAreaIsMainWorld(uint mapID)
        {
            uint P3 = (mapID & 0xFF000000U) >> 24;
            return P3 == MAIN_WORLD_ID;
        }
        public static bool mapAreaIsDLC(uint mapID)
        {
            uint P3 = (mapID & 0xFF000000U) >> 24;
            return P3 == DLC_WORLD_ID;
        }

        public static (float, float) getMapIDWorldMapCoords(uint mapID)
        {//see http://soulsmodding.wikidot.com/reference:elden-ring-map-list
            if (!mapAreaIsMainWorld(mapID) && !mapAreaIsDLC(mapID)) { return (float.NaN, float.NaN); }
            uint P3 = (mapID & 0xFF000000U) >> 24;
            uint P2 = (mapID & 0x00FF0000U) >> 16;
            uint P1 = (mapID & 0x0000FF00U) >> 8;
            uint P0 = (mapID & 0x000000FFU); //should we check if this is 0? only problem is if the grid is not using the small-tile 256
            float X = P2 * TILE_SIZE;
            float Z = P1 * TILE_SIZE;
            return (X, Z);
        }
        public static (float, float, float, uint) getWorldMapCoords((float, float, float, float, uint) mapCoords, bool justConvPos = false)
        {//disambiguate by always returning the 'area number'/'world id'
            if (mapAreaIsMainWorld(mapCoords.Item5) || mapAreaIsDLC(mapCoords.Item5))
            {
                var mapOff = getMapIDWorldMapCoords(mapCoords.Item5);
                return (mapCoords.Item1 + mapOff.Item1, mapCoords.Item2, mapCoords.Item3 + mapOff.Item2, (mapCoords.Item5 & 0xFF000000U) >> 24);
            }
            else
            {
                var mapID = mapCoords.Item5;
                uint P3 = (mapID & 0xFF000000U) >> 24;
                uint P2 = (mapID & 0x00FF0000U) >> 16;
                uint P1 = (mapID & 0x0000FF00U) >> 8;
                //try and find a direct conversion back to either main world
                //can't rely on the grid system for dungeons so look for an exact match. there should be one for anywhere the world map works.
                foreach (var e in MapConvDB.MapConvEntries)
                {
                    if (e.intVals["srcAreaNo"] == P3 && e.intVals["srcGridXNo"] == P2 && e.intVals["srcGridZNo"] == P1
                        && (e.intVals["dstAreaNo"] == MAIN_WORLD_ID || e.intVals["dstAreaNo"] == DLC_WORLD_ID))
                    {
                        var localXOffset = mapCoords.Item1 - e.floatVals["srcPosX"];
                        var localYOffset = mapCoords.Item2 - e.floatVals["srcPosY"];
                        var localZOffset = mapCoords.Item3 - e.floatVals["srcPosZ"];
                        var targetX = e.floatVals["dstPosX"] + (justConvPos ? 0.0f : localXOffset); //if justConvPos is set, get the coords of the corresponding world map position only, rather than actually offsetting it to the player position
                        var targetY = e.floatVals["dstPosY"] + (justConvPos ? 0.0f : localYOffset);
                        var targetZ = e.floatVals["dstPosZ"] + (justConvPos ? 0.0f : localZOffset);
                        var targetGridX = e.intVals["dstGridXNo"];
                        var targetGridZ = e.intVals["dstGridZNo"];
                        var targetXGlobal = targetX + targetGridX * TILE_SIZE;
                        var targetYGlobal = targetY;
                        var targetZGlobal = targetZ + targetGridZ * TILE_SIZE;

                        return (targetXGlobal, targetYGlobal, targetZGlobal, (uint)e.intVals["dstAreaNo"]);
                    }
                }
                Utils.debugWrite("Cannot get coords for: " + mapCoords.ToString());
                return (float.NaN, float.NaN, float.NaN, 0);
            }
        }
        public static string mapCoordsToString((float, float, float, float, uint) coords)
        {
            return coords.Item1 + "," + coords.Item2 + "," + coords.Item3 + "," + coords.Item4 + "," + coords.Item5;
        }
        public static (float, float, float, float, uint) mapCoordsFromString(string coords)
        {
            try
            {
                var spl = coords.Split(',');
                var x = float.Parse(spl[0]);
                var y = float.Parse(spl[1]);
                var z = float.Parse(spl[2]);
                var rotation = float.Parse(spl[3]);
                var mapID = uint.Parse(spl[4]);
                return (x, y, z, rotation, mapID);
            }
            catch { }
            return (float.NaN, float.NaN, float.NaN, float.NaN, 0xFFFFFFFF);
        }
    }

    public class MapConvDB
    {
        public static List<MapConvEntry> MapConvEntries = new List<MapConvEntry>();
        static MapConvDB()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetManifestResourceNames()
              .Single(str => str.EndsWith("WorldMapLegacyConvParamTrim.csv")); //dumped with param/mapstudio or yapped, then some unused columns were deleted.

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                var header = reader.ReadLine();
                var headerSplit = header.Split(',');
                string line = "";
                while ((line = reader.ReadLine()) != null)
                {
                    var entry = new MapConvEntry();
                    var lineSplit = line.Split(',');
                    for (int i = 0; i < headerSplit.Length; i++)
                    {
                        bool isFloatVal = headerSplit[i].Contains("Pos");
                        if (isFloatVal)
                        {
                            if (!float.TryParse(lineSplit[i], out var val)) { continue; }
                            entry.floatVals[headerSplit[i]] = val;
                        }
                        else
                        {
                            if (!int.TryParse(lineSplit[i], out var val)) { continue; }
                            entry.intVals[headerSplit[i]] = val;
                        }
                    }
                    MapConvEntries.Add(entry);
                }
            }
        }
        public class MapConvEntry
        {
            public Dictionary<string, int> intVals = new Dictionary<string, int>();
            public Dictionary<string, float> floatVals = new Dictionary<string, float>();
            public override string ToString()
            {
                string ret = "";
                foreach (var kvp in intVals) { ret += kvp.Key + " [" + kvp.Value + "] "; }
                foreach (var kvp in floatVals) { ret += kvp.Key + " [" + kvp.Value + "] "; }
                return ret;
            }
        }
    }

    public class ItemDB
    {
        static List<(string, uint, ItemCategory)> items = new System.Collections.Generic.List<(string, uint, ItemCategory)>();
        static List<(string, uint, ItemCategory)> infusions = new System.Collections.Generic.List<(string, uint, ItemCategory)>();
        static List<(string, uint, ItemCategory)> ashes = new System.Collections.Generic.List<(string, uint, ItemCategory)>();
        static bool _loaded = false;

        static List<(string, uint, ItemCategory)> importNameIDCSV(string name, System.Globalization.NumberStyles numType = System.Globalization.NumberStyles.HexNumber)
        {
            var ret = new List<(string, uint, ItemCategory)>();
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith(name));
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    var header = reader.ReadLine(); // throw away header
                    string line = "";
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        // get category
                        int lastComma = line.LastIndexOf(',');
                        if (lastComma < 0) continue;
                        var category = Enum.TryParse<ItemCategory>(line.Substring(lastComma + 1), ignoreCase: true, out var parsed)
                            ? parsed
                            : ItemCategory.NONE;

                        // get id
                        string beforeLast = line.Substring(0, lastComma);
                        int secondLastComma = beforeLast.LastIndexOf(',');
                        if (secondLastComma < 0) continue;
                        string idStr = beforeLast.Substring(secondLastComma + 1).Trim();
                        var id = uint.Parse(idStr, numType);

                        // get name
                        string itemName = beforeLast.Substring(0, secondLastComma).Trim();

                        ret.Add((itemName, id, category));
                    }
                }
                return ret;
            }
            catch
            {
                return ret;
            }
        }

        static void loadDB()
        {
            if (_loaded) { return; }

            var itemsTemp = importNameIDCSV("items.csv");
            infusions = importNameIDCSV("infusions.csv", System.Globalization.NumberStyles.Number);
            //sort order can be a bit jank so just re sort it now
            itemsTemp.Sort((x, y) =>
            {
                bool xIsAbout = x.Item1.StartsWith("About ", StringComparison.OrdinalIgnoreCase);
                bool yIsAbout = y.Item1.StartsWith("About ", StringComparison.OrdinalIgnoreCase);

                if (xIsAbout && !yIsAbout) return 1;
                if (!xIsAbout && yIsAbout) return -1;

                if (x.Item1 == y.Item1) { return x.Item2.CompareTo(y.Item2); }
                return x.Item1.CompareTo(y.Item1);
            });

            var nameSet = new HashSet<string>();
            var dupeSet = new HashSet<string>();
            foreach (var item in itemsTemp)
            {
                if (nameSet.Contains(item.Item1)) { dupeSet.Add(item.Item1); } else { nameSet.Add(item.Item1); }
            }

            var dupeCount = new Dictionary<string, int>();
            items.Clear();
            foreach (var item in itemsTemp)
            {
                string name = item.Item1;
                if (dupeSet.Contains(name))
                {
                    int count = 0;
                    dupeCount.TryGetValue(name, out count);
                    dupeCount[name] = ++count;
                    name = $"{name} #{count}";
                }
                items.Add((name, item.Item2, item.Item3));
            }

            ashes = items.Where(x => x.Item1.StartsWith("Ash of War: ", StringComparison.OrdinalIgnoreCase))
                .Select(x =>
                {
                    string nameWithoutPrefix = x.Item1.Substring("Ash of War: ".Length).Trim();
                    return (nameWithoutPrefix, x.Item2, x.Item3);
                })
                .ToList();
            ashes.Insert(0, ("Default", 0xFFFFFFFF, ItemCategory.NONE));

            _loaded = true;
        }

        public static List<(string, uint, ItemCategory)> Items
        {
            get
            {
                loadDB();
                return items;
            }
        }
        public static List<(string, uint, ItemCategory)> Infusions
        {
            get
            {
                loadDB();
                return infusions;
            }
        }
        public static List<(string, uint, ItemCategory)> Ashes
        {
            get
            {
                loadDB();
                return ashes;
            }
        }
    }

    public class GoodsEvents
    {
        static bool _loaded = false;
        static List<(string, int, int)> data = new List<(string, int, int)>();
        static void load()
        {
            var list = FileUtils.importGenericTextResource("GoodsEvents.tsv", '\t');
            foreach (var row in list.Skip(1)) //skip headers
            {
                var name = row[2];
                var evtId = row[0];
                var itemId = row[1];
                if (int.TryParse(evtId, out var evtIdInt) && int.TryParse(itemId, out var itemIdInt))
                {
                    data.Add((name, evtIdInt, itemIdInt));
                }
            }
            _loaded = true;
        }
        public static int getEvent(uint goodsItemID)
        {
            if (!_loaded) { load(); }
            var row = data.Where(x => x.Item3 == goodsItemID).FirstOrDefault();
            if (row.Item1 == null) { return -1; }
            return row.Item2;
        }
    }

    public class FlagDB
    {
        static bool _loaded = false;
        static void load()
        {
            _data.Add("Base Maps", importIDNameTsv("BaseMaps.tsv"));
            _data.Add("DLC Maps", importIDNameTsv("DLCMaps.tsv"));
            //_data.Add("Base Graces", importIDNameTsv("BaseGraces.tsv"));
            //_data.Add("DLC Graces", importIDNameTsv("DLCGraces.tsv"));
            _data.Add("Base Bosses", importIDNameTsv("BaseBosses.tsv"));
            _data.Add("DLC Bosses", importIDNameTsv("DLCBosses.tsv"));
            _data.Add("Cookbooks & Crafting Kit", importIDNameTsv("BaseCookbooks.tsv"));
            _data.Add("DLC Cookbooks", importIDNameTsv("DLC Cookbooks.tsv"));
            _data.Add("Whetblades", importIDNameTsv("Whetblade.tsv"));
            _loaded = true;
        }
        public static List<(string, int)> importIDNameTsv(string file)
        {
            var ret = new List<(string, int)>();
            var list = FileUtils.importGenericTextResource(file, '\t');
            foreach (var row in list.Skip(1)) //skip headers
            {
                if (row.Length < 2) { continue; }
                var name = row[1];
                var evtId = row[0];
                if (int.TryParse(evtId, out var evtIdInt))
                {
                    ret.Add((name, evtIdInt));
                }
            }
            return ret;
        }
        static Dictionary<string, List<(string, int)>> _data = new Dictionary<string, List<(string, int)>>();
        public static Dictionary<string, List<(string, int)>> data
        {
            get
            {
                if (!_loaded) { load(); }
                return _data;
            }
        }
    }

    public class GraceDB
    {
        static List<Grace> graces = new List<Grace>();

        public GraceDB(bool supportsDlc)
        {
            graces = importIDNameTsv("BaseGraces.tsv");
            if (supportsDlc)
            {
                var dlcGraces = importIDNameTsv("DLCGraces.tsv");
                graces.AddRange(dlcGraces);
            }
        }

        public static List<Grace> importIDNameTsv(string file)
        {
            var ret = new List<Grace>();
            var list = FileUtils.importGenericTextResource(file, '\t');
            foreach (var row in list.Skip(1))
            {
                if (row.Length < 2) continue;

                if (!int.TryParse(row[0], out int id))
                    continue;

                var segments = row[1].Split(new[] { " - " }, StringSplitOptions.None);

                var area = segments.Length > 0 ? segments[0] : "";
                var subArea = segments.Length > 1 ? segments[1] : "";
                var name = segments.Length > 2 ? string.Join(" - ", segments.Skip(2)) : "";

                ret.Add(new Grace(id, area, subArea, name));
            }
            return ret;
        }

        public static List<Grace> Graces => graces;
    }

    public class Grace
    {
        public int ID { get; set; }
        public string Area { get; set; }
        public string SubArea { get; set; }
        public string Name { get; set; }

        public Grace(int id, string area, string subArea, string name)
        {
            ID = id;
            Area = area;
            SubArea = subArea;
            Name = name;
        }
    }

    public class SubAreaGroup
    {
        public string SubArea { get; set; }
        public List<Grace> Graces { get; set; } = new List<Grace>();
    }

    public class AreaGroup
    {
        public string Area { get; set; }
        public List<SubAreaGroup> SubAreas { get; set; } = new List<SubAreaGroup>();
    }

    public class ExtraFlag
    {
        public int id { get; set; } = -1;
        public string name { get; set; } = "";
        public bool state { get; set; } = false;
        public override string ToString()
        {
            return $"{name} ({id}): {state}";
        }
        public static ExtraFlag parse(string str)
        {
            if (string.IsNullOrEmpty(str)) { return null; }
            var spl = str.Split(',');
            if (spl.Length < 2) { return null; }
            var ret = new ExtraFlag();
            if (!int.TryParse(spl[0], out var id)) { return null; }
            ret.id = id;
            ret.name = spl[1];
            //state is not stored
            return ret;
        }
    }

    public class TeleportLocation
    {
        (float, float, float, float, uint) coords;
        string name;

        public TeleportLocation(string dbString)
        {
            coords = TeleportHelper.mapCoordsFromString(dbString);
            name = string.Join(",", dbString.Split(',').Skip(5));
        }

        public (float, float, float, float, uint) getCoords()
        {
            return coords;
        }

        public override string ToString()
        {
            return name;
        }
    }
}



