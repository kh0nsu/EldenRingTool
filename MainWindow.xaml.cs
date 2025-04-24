using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Threading;
using System.Reflection;
using MiscUtils;

namespace EldenRingTool
{
    public partial class MainWindow : Window, IDisposable
    {
        //TODO: reorganise a bit.

        [DllImport("User32.dll")]
        private static extern bool RegisterHotKey(
            [In] IntPtr hWnd,
            [In] int id,
            [In] uint fsModifiers,
            [In] uint vk);

        [DllImport("User32.dll")]
        private static extern bool UnregisterHotKey(
            [In] IntPtr hWnd,
            [In] int id);

        const int WM_HOTKEY = 0x0312;

        [Flags]
        public enum Modifiers
        {
            NO_MOD = 0x0000,
            ALT = 0x0001,
            CTRL = 0x0002,
            SHIFT = 0x0004,
            WIN = 0x0008
        }

        public enum HOTKEY_ACTIONS
        {
            QUITOUT = 1,
            TELEPORT_SAVE,
            TELEPORT_LOAD,
            YEET_FORWARD, YEET_UP, YEET_DOWN, YEET_PLUS_X, YEET_MINUS_X, YEET_PLUS_Z, YEET_MINUS_Z,
            KILL_TARGET, FREEZE_TARGET_HP,
            COL_MESH_A, COL_MESH_B, COL_MESH_CYCLE,
            CHAR_MESH, HIDE_MODELS,
            HITBOX_A, HITBOX_B,
            NO_DEATH, ALL_NO_DEATH,
            ONE_HP, MAX_HP, DIE, RUNE_ARC, SET_HP_LAST,
            DISABLE_AI, REPEAT_ENEMY_ACTIONS,
            INF_STAM, INF_FP, INF_CONSUM, ONE_SHOT,
            NO_GRAVITY, NO_MAP_COL,
            TORRENT_NO_DEATH, TORRENT_NO_GRAV, TORRENT_NO_MAP_COL,
            POISE_VIEW,
            SOUND_VIEW, TARGETING_VIEW,
            EVENT_VIEW, EVENT_STOP,
            FREE_CAMERA, FREE_CAMERA_CONTROL, NO_CLIP, ALLOW_MAP_COMBAT, TORRENT_ANYWHERE,
            DISABLE_STEAM_INPUT_ENUM, DISABLE_STEAM_ACHIEVEMENTS, MUTE_MUSIC,
            ADD_SOULS,
            GAME_SPEED_25PC, GAME_SPEED_50PC, GAME_SPEED_75PC, GAME_SPEED_100PC, GAME_SPEED_150PC, GAME_SPEED_200PC, GAME_SPEED_300PC, GAME_SPEED_500PC, GAME_SPEED_1000PC,
            FPS_30, FPS_60, FPS_120, FPS_144, FPS_240, FPS_1000,
            FPS, //arbitrary fps
            TOGGLE_STATS_FULL, TOGGLE_RESISTS, TOGGLE_DEFENSES, TOGGLE_COORDS,
            ENABLE_TARGET_HOOK, STAY_ON_TOP,
            GREAT_RUNE, PHYSICK, ASHES, SPELLS,
        }

        public class HotkeyAction
        {
            public HOTKEY_ACTIONS actID { get; set; } = 0;
            public bool needsParam()
            {
                return actID == HOTKEY_ACTIONS.FPS;
            }
            public string someParam { get; set; } = null;
            public override string ToString()
            {
                var ret = actID.ToString();
                if (someParam != null) { ret += " " + someParam.ToString(); }
                return ret;
            }
        }

        ERProcess _process = null;
        private bool disposedValue;

        System.Windows.Threading.DispatcherTimer _timer = new System.Windows.Threading.DispatcherTimer();
        string _normalTitle = "";

        bool _hooked = false;

        const string hotkeyFileName = "ertool_hotkeys.txt";

        Dictionary<string, Modifiers> modMap = new Dictionary<string, Modifiers>();
        Dictionary<string, HOTKEY_ACTIONS> actionMap = new Dictionary<string, HOTKEY_ACTIONS>();
        Dictionary<string, Key> keyMap = new Dictionary<string, Key>();

        Dictionary<int, List<HotkeyAction>> registeredHotkeys = new Dictionary<int, List<HotkeyAction>>();

        (float, float, float) lastPos = (0, 0, 0);
        (float, float, float) diffNormalisedLpf = (0, 0, 0);
#if DEBUG
        (float, float, float, float, uint)? lastMapPos = null;
#endif

        const float YEET_AMOUNT = 1;
        int recentlyYeetedCounter = 0;

        (float, float, float, float, uint)? savedMapPos = null;

        const string websiteUrl = @"https://ds3tool.s3.ap-southeast-2.amazonaws.com/tools.html";
        const string updateCheckUrl = @"https://ds3tool.s3.ap-southeast-2.amazonaws.com/ERToolUpdates.txt";

        bool updateCheckStartupCheckDone = false;

        bool _freeCamFirstActivation = true;

        bool isCompact = false;

        bool _playerNoDeathStateWas = false;
        bool _torNoDeathStateWas = false;
        bool _noClipActive = false;
        bool panelsCollapsed = false;

        static string windowStateFile()
        {
            return Utils.getFnameInAppdata("windowstate.txt", "ERTool");
        }

        static string getHotkeyFileAppData()
        {
            return Utils.getFnameInAppdata(hotkeyFileName, "ERTool");
        }

        static string hotkeyFile()
        {//local file can override (an older tool version used a local file)
            if (File.Exists(hotkeyFileName)) { return hotkeyFileName; }
            return getHotkeyFileAppData();
        }
        static string getUpdCheckFile()
        {
            return Utils.getFnameInAppdata("DisableUpdateCheck", "ERTool");
        }

        static string posDbFile()
        {
            return Utils.getFnameInAppdata("saved_positions.txt", "ERTool");
        }

        static string extraFlagsFile()
        {
            return Utils.getFnameInAppdata("extra_flags.txt", "ERTool");
        }

        public MainWindow()
        {
            InitializeComponent();
            var assInfo = Assembly.GetEntryAssembly().GetName();
            Title = "ERTool v" + assInfo.Version;
            _normalTitle = Title;

            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            Loaded += MainWindow_Loaded;

            retry:
            try
            {
                _process = new ERProcess();
            }
            catch { _process = null; }
            if (null == _process)
            {
                var res = MessageBox.Show("Could not attach to the game. This could be because it's not running, or because it was blocked by Easy Anti Cheat or by anti virus.\r\n\r\nClick Yes to try launching the game with EAC disabled, or No to just try attaching again.", "hobbWeird", MessageBoxButton.YesNoCancel);
                if (res == MessageBoxResult.Yes)
                {
                    if (!LaunchUtils.launchGame())
                    {
                        MessageBox.Show("Could not launch game.", "Sadge", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {//success but wait a bit for it to start.
                        for (int i = 0; i < 30; i++)
                        {
                            System.Threading.Thread.Sleep(1000);
                            if (ERProcess.checkGameRunning())
                            {
                                System.Threading.Thread.Sleep(1000); //arbitrary wait to let the game start up more before applying patches. likely not required.
                                break;
                            }
                        }
                    }
                    goto retry;
                }
                else if (res == MessageBoxResult.No)
                {
                    goto retry;
                }
                Close();
            }
            else
            {//we good
                _process.patchLogos();
                _timer.Tick += _timer_Tick;
                _timer.Interval = TimeSpan.FromSeconds(0.1); //~10hz UI update rate
                _timer.Start();
            }

        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                var windowInfo = 
                    $"{Left} " +
                    $"{Top} " +
                    $"{isCompact} " +
                    $"{chkSteamInputEnum.IsChecked} " +
                    $"{chkSteamAchieve.IsChecked} " +
                    $"{chkMuteMusic.IsChecked} " +
                    $"{PlayerPanel.Visibility} " +
                    $"{TorrentPanel.Visibility} " +
                    $"{EnemyPanel.Visibility} " +
                    $"{MovementPanel.Visibility} " +
                    $"{TeleportPanel.Visibility} " +
                    $"{HitboxPanel.Visibility} " +
                    $"{MeshPanel.Visibility} " +
                    $"{ViewsPanel.Visibility} " +
                    $"{MiscPanel.Visibility} " +
                    $"{QoLPanel.Visibility} " +
                    $"{hpPoisePanel.Visibility} " +
                    $"{resistsPanel.Visibility} " +
                    $"{defensesPanel.Visibility}";

                File.WriteAllText(windowStateFile(), windowInfo);
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }

        void loadWindowState()
        {
            try
            {
                var windowInfo = File.ReadAllText(windowStateFile());
                if (string.IsNullOrEmpty(windowInfo)) { return; }
                var spl = windowInfo.Split(' ');
                var left = double.Parse(spl[0]);
                var top = double.Parse(spl[1]);
                bool compact = bool.Parse(spl[2]);
                if ((left + Width) > System.Windows.SystemParameters.VirtualScreenWidth || (top + Height) > System.Windows.SystemParameters.VirtualScreenHeight)
                {
                    Console.WriteLine("Not restoring position, would go off-screen");
                }
                else
                {
                    Left = left;
                    Top = top;
                }
                if (compact) { setCompact(); } //default full

                if (spl.Length >= 6)
                {
                    chkSteamInputEnum.IsChecked = bool.Parse(spl[3]);
                    chkSteamAchieve.IsChecked = bool.Parse(spl[4]);
                    chkMuteMusic.IsChecked = bool.Parse(spl[5]);
                }
                
                if (spl.Length >= 9)
                {
                    RestorePanelVisibility(PlayerPanelControl, PlayerPanel, spl[6]);
                    RestorePanelVisibility(TorrentPanelControl, TorrentPanel, spl[7]);
                    RestorePanelVisibility(EnemyPanelControl, EnemyPanel, spl[8]);
                    RestorePanelVisibility(MovementPanelControl, MovementPanel, spl[9]);
                    RestorePanelVisibility(TeleportPanelControl, TeleportPanel, spl[10]);
                    RestorePanelVisibility(HitboxPanelControl, HitboxPanel, spl[11]);
                    RestorePanelVisibility(MeshPanelControl, MeshPanel, spl[12]);
                    RestorePanelVisibility(ViewsPanelControl, ViewsPanel, spl[13]);
                    RestorePanelVisibility(MiscPanelControl, MiscPanel, spl[14]);
                    RestorePanelVisibility(QoLPanelControl, QoLPanel, spl[15]);
                    RestorePanelVisibility(hpPoisePanelControl, hpPoisePanel, spl[16]);
                    RestorePanelVisibility(resistsPanelControl, resistsPanel, spl[17]);
                    RestorePanelVisibility(defensesPanelControl, defensesPanel, spl[18]);

                }
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }

        //TODO: move hotkey stuff to utils?

        void setUpMapsForHotkeys()
        {
            foreach (var mod in Enum.GetValues(typeof(Modifiers)))
            {
                modMap[mod.ToString()] = (Modifiers)mod;
            }
            foreach (var act in Enum.GetValues(typeof(HOTKEY_ACTIONS)))
            {
                actionMap[act.ToString()] = (HOTKEY_ACTIONS)act;
            }
            foreach (var k in Enum.GetValues(typeof(Key)))
            {
                keyMap[k.ToString()] = (Key)k;
            }
        }

        bool parseHotkeys(string linesStr = null)
        {
            try
            {
                string[] lines;
                if (!string.IsNullOrEmpty(linesStr))
                {
                    lines = linesStr.Split('\r', '\n');
                }
                else
                {
                    lines = File.ReadAllLines(hotkeyFile());
                }

                var hotkeyMap = new Dictionary<(Key, Modifiers), List<HotkeyAction>>();
                foreach (var line in lines)
                {
                    if (line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("//")) { continue; }
                    var modifiers = Modifiers.NO_MOD;
                    HotkeyAction action = null;
                    Key? hotkey = null;
                    var spl = line.Split(' ');
                    for (int j = 0; j < spl.Length; j++)
                    {
                        var s = spl[j];
                        if (modMap.ContainsKey(s)) { modifiers |= modMap[s]; }
                        if (keyMap.ContainsKey(s)) { hotkey = keyMap[s]; }
                        if (actionMap.ContainsKey(s))
                        {
                            action = new HotkeyAction() { actID = actionMap[s] };
                            if (action.needsParam())
                            {//param is meant to be right after
                                var paramInd = j + 1;
                                if (spl.Length > paramInd)
                                {
                                    var paramStr = spl[paramInd];
                                    action.someParam = paramStr;
                                    continue; //no further processing
                                }
                            }
                        }
                    }
                    if (action != null && hotkey.HasValue)
                    {
                        var key = (hotkey.Value, modifiers);
                        if (!hotkeyMap.ContainsKey(key))
                        {
                            hotkeyMap.Add(key, new List<HotkeyAction>());
                        }
                        hotkeyMap[key].Add(action);
                    }
                }

                int i = 0;
                foreach (var kvp in hotkeyMap)
                {
                    registeredHotkeys.Add(i, kvp.Value);
                    RegisterHotKey(new WindowInteropHelper(this).Handle, i, (uint)kvp.Key.Item2, (uint)KeyInterop.VirtualKeyFromKey(kvp.Key.Item1));
                    var debugStr = $"Hotkey {i} set: {kvp.Key} ->";
                    foreach (var act in kvp.Value)
                    {
                        debugStr += " " + act.ToString();
                    }
                    Utils.debugWrite(debugStr);
                    i++;
                }
                btnHotkeys.Foreground = registeredHotkeys.Count > 0 ? Brushes.Blue : Brushes.Black;
                return true;
            }
            catch { }
            return false;
        }

        void clearRegisteredHotkeys()
        {
            foreach (var h in registeredHotkeys)
            {
                UnregisterHotKey(new WindowInteropHelper(this).Handle, h.Key);
            }
            registeredHotkeys.Clear();
        }

        string generateDefaultHotkeyFile(bool writeOut = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine(";Set hotkeys below. Some example hotkeys are provided.");
            sb.AppendLine(";If you don't want to use hotkeys, just remove all the hotkeys listed.");
            sb.AppendLine(";Restart ERTool after updating the hotkeys, or ctrl+click the hotkeys button.");
            sb.AppendLine(";Lines starting with ; are ignored.");
            sb.AppendLine(";All text is case-sensitive.");
            sb.AppendLine(";Note that these are global hotkeys and may conflict with other applications. If a given key doesn't work, try using a modifier. (Eg. F12 may not work but CTRL F12 should.) Some of the more obscure keys may also not work.");
            sb.AppendLine(";To generate a fresh hotkey file, alt+click the hotkey setup button.");
            sb.Append(";Valid actions:");
            foreach (var kvp in actionMap) { sb.Append(" " + kvp.Key); }
            sb.AppendLine();
            sb.Append(";Valid modifier keys:");
            foreach (var kvp in modMap) { sb.Append(" " + kvp.Key); }
            sb.AppendLine();
            sb.Append(";Valid keys:");
            foreach (var kvp in keyMap) { sb.Append(" " + kvp.Key); }
            sb.AppendLine();

            sb.AppendLine($"{Modifiers.CTRL} {Modifiers.SHIFT} {Key.Z} {HOTKEY_ACTIONS.QUITOUT}");
            sb.AppendLine($"{Modifiers.CTRL} {Modifiers.SHIFT} {Key.C} {HOTKEY_ACTIONS.TELEPORT_SAVE}");
            sb.AppendLine($"{Modifiers.CTRL} {Modifiers.SHIFT} {Key.V} {HOTKEY_ACTIONS.TELEPORT_LOAD}");
            sb.AppendLine($"{Modifiers.CTRL} {Modifiers.SHIFT} {Key.K} {HOTKEY_ACTIONS.KILL_TARGET}");

            if (writeOut) { File.WriteAllText(hotkeyFile(), sb.ToString()); }
            return sb.ToString();
        }

        void backupHotkeyFile()
        {
            try
            {
                File.Copy(hotkeyFile(), hotkeyFile() + ".bak", true);
            }
            catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                //register for message passing
                var source = PresentationSource.FromVisual(this as Visual) as HwndSource;
                if (null == source) { Utils.debugWrite("Could not make hwnd source"); }
                source.AddHook(WndProc);

                //hotkeys
                setUpMapsForHotkeys();

                if (File.Exists(hotkeyFileName) && !File.Exists(getHotkeyFileAppData()))
                {
                    MessageBox.Show("Hotkey mapping will be moved to AppData. Shift-click Hotkey Setup if you need to access this folder.");
                    File.Move(hotkeyFileName, getHotkeyFileAppData());
                }

                if (File.Exists(hotkeyFile()))
                {
                    if (!parseHotkeys())
                    {
                        MessageBox.Show("Failed to parse hotkey file.");
                    }
                }
                else
                {//none by default
                }
            }
            catch(Exception ex) { Utils.debugWrite(ex.ToString()); }

            loadWindowState(); //restore last state if saved

            maybeDoUpdateCheck();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                Utils.debugWrite($"Got hotkey id {id}");
                if (!registeredHotkeys.ContainsKey(id))
                {
                    Utils.debugWrite($"Invalid hotkey {id}");
                }
                else
                {
                    var actList = registeredHotkeys[id];
                    foreach (var act in actList)
                    {
                        Utils.debugWrite($"Doing action {act}");
                        doAct(act);
                    }
                }
            }

            return IntPtr.Zero;
        }

        void doAct(HotkeyAction action)
        {
            var act = action.actID;
            switch (act)
            {
                case HOTKEY_ACTIONS.QUITOUT: doQuitout(null, null); break;
                case HOTKEY_ACTIONS.TELEPORT_SAVE: savePos(null, null); break;
                case HOTKEY_ACTIONS.TELEPORT_LOAD: restorePos(null, null); break;
                case HOTKEY_ACTIONS.YEET_FORWARD: doYeet("Forward", null); break;
                case HOTKEY_ACTIONS.YEET_UP: doYeet("+Y", null); break;
                case HOTKEY_ACTIONS.YEET_DOWN: doYeet("-Y", null); break;
                case HOTKEY_ACTIONS.YEET_PLUS_X: doYeet("+X", null); break;
                case HOTKEY_ACTIONS.YEET_MINUS_X: doYeet("-X", null); break;
                case HOTKEY_ACTIONS.YEET_PLUS_Z: doYeet("+Z", null); break;
                case HOTKEY_ACTIONS.YEET_MINUS_Z: doYeet("-Z", null); break;
                case HOTKEY_ACTIONS.KILL_TARGET: killTarget(null, null); break;
                case HOTKEY_ACTIONS.FREEZE_TARGET_HP: chkFreezeHP.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.COL_MESH_A: chkColMeshA.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.COL_MESH_B: chkColMeshB.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.COL_MESH_CYCLE: changeMeshColours(null, null); break;
                case HOTKEY_ACTIONS.CHAR_MESH: chkCharMesh.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.HIDE_MODELS: chkHideModels.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.HITBOX_A: chkHitboxA.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.HITBOX_B: chkHitboxB.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.NO_DEATH: chkPlayerNoDeath.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.ALL_NO_DEATH: chkAllNoDeath.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.ONE_HP: chkOneHP.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.MAX_HP: chkMaxHP.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.SET_HP_LAST: if (lastSetHP.HasValue) { _process.getSetPlayerHP(lastSetHP.Value); } break;
                case HOTKEY_ACTIONS.DIE: instantDeath(null, null); break;
                case HOTKEY_ACTIONS.RUNE_ARC: chkRuneArc.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.DISABLE_AI: chkDisableAI.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.REPEAT_ENEMY_ACTIONS: chkRepeatAction.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.INF_STAM: chkInfStam.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.INF_FP: chkInfFP.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.INF_CONSUM: chkInfConsum.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.ONE_SHOT: chkOneShot.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.NO_GRAVITY: chkPlayerNoGrav.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.NO_MAP_COL: chkPlayerNoMapCol.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.TORRENT_NO_DEATH: chkTorNoDeath.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.TORRENT_NO_GRAV: chkTorNoGrav.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.TORRENT_NO_MAP_COL: chkTorNoMapCol.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.POISE_VIEW: chkPoiseView.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.SOUND_VIEW: chkSoundView.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.TARGETING_VIEW: chkTargetingView.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.EVENT_VIEW: chkEventView.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.EVENT_STOP: chkEventStop.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.FREE_CAMERA: chkFreeCam.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.FREE_CAMERA_CONTROL: chkFreeCamControl.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.NO_CLIP: chkNoClip.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.ALLOW_MAP_COMBAT: chkCombatMap.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.TORRENT_ANYWHERE: chkTorrentAnywhere.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.DISABLE_STEAM_INPUT_ENUM: chkSteamInputEnum.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.DISABLE_STEAM_ACHIEVEMENTS: chkSteamAchieve.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.MUTE_MUSIC: chkMuteMusic.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.ADD_SOULS: addSouls(null, null); break;
                case HOTKEY_ACTIONS.GAME_SPEED_25PC: _process.getSetGameSpeed(0.25f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_50PC: _process.getSetGameSpeed(0.5f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_75PC: _process.getSetGameSpeed(0.75f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_100PC: _process.getSetGameSpeed(1.0f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_150PC: _process.getSetGameSpeed(1.5f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_200PC: _process.getSetGameSpeed(2.0f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_300PC: _process.getSetGameSpeed(3.0f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_500PC: _process.getSetGameSpeed(5.0f); break;
                case HOTKEY_ACTIONS.GAME_SPEED_1000PC: _process.getSetGameSpeed(10.0f); break;
                case HOTKEY_ACTIONS.FPS_30: _process.getSetFrameTimeTarget(1 / 30.0f); break;
                case HOTKEY_ACTIONS.FPS_60: _process.getSetFrameTimeTarget(1 / 60.0f); break;
                case HOTKEY_ACTIONS.FPS_120: _process.getSetFrameTimeTarget(1 / 120.0f); break;
                case HOTKEY_ACTIONS.FPS_144: _process.getSetFrameTimeTarget(1 / 144.0f); break;
                case HOTKEY_ACTIONS.FPS_240: _process.getSetFrameTimeTarget(1 / 240.0f); break;
                case HOTKEY_ACTIONS.FPS_1000: _process.getSetFrameTimeTarget(1 / 1000.0f); break;
                case HOTKEY_ACTIONS.FPS:
                    {
                        var targetFps = 60.0f;
                        if (!string.IsNullOrEmpty(action.someParam) && float.TryParse(action.someParam, out var targetFpsOut))
                        {
                            targetFps = targetFpsOut;
                        }
                        else
                        {
                            Utils.debugWrite("Error parsing fps");
                        }
                        var frameTime = 1 / targetFps;
                        _process.getSetFrameTimeTarget(frameTime);
                    }
                    break;
                case HOTKEY_ACTIONS.TOGGLE_STATS_FULL: toggleStatsFull(null, null); break;
                case HOTKEY_ACTIONS.TOGGLE_RESISTS: toggleResists(null, null); break;
                case HOTKEY_ACTIONS.TOGGLE_DEFENSES: toggleDefenses(null, null); break;
                case HOTKEY_ACTIONS.TOGGLE_COORDS: toggleCoords(null, null); break;
                case HOTKEY_ACTIONS.ENABLE_TARGET_HOOK: installTargetHook(targetHookButton, null); break;
                case HOTKEY_ACTIONS.STAY_ON_TOP: chkStayOnTop.IsChecked ^= true; break;
                case HOTKEY_ACTIONS.GREAT_RUNE: _process.openMenuByName(_process.MENUS[1]); break;
                case HOTKEY_ACTIONS.PHYSICK: _process.openMenuByName(_process.MENUS[2]); break;
                case HOTKEY_ACTIONS.ASHES: _process.openMenuByName(_process.MENUS[3]); break;
                case HOTKEY_ACTIONS.SPELLS: _process.openMenuByName(_process.MENUS[5]); break;
                default: Utils.debugWrite("Action not handled: " + act.ToString()); break;
            }
        }

        void updateTargetInfo()
        {
            var hp = _process.getSetTargetInfo(ERProcess.TargetInfo.HP);
            var hpmax = _process.getSetTargetInfo(ERProcess.TargetInfo.MAX_HP);
            var poise = _process.getSetTargetInfo(ERProcess.TargetInfo.POISE);
            var poisemax = _process.getSetTargetInfo(ERProcess.TargetInfo.MAX_POISE);
            var poisetimer = _process.getSetTargetInfo(ERProcess.TargetInfo.POISE_TIMER);
            //Console.WriteLine($"{hp} {hpmax} {poise} {poisemax} {poisetimer}");
            if (double.IsNaN(hp)) { return; }
            
            if (hpBar.Value > hpmax) { hpBar.Value = 0; }
            hpBar.Maximum = hpmax;
            hpBar.Value = hp > 0 ? hp : 0;
            hpText.Text = $"HP: {(int)hp} / {(int)hpmax}";

            if (poiseBar.Value > poisemax) { poiseBar.Value = 0; }
            poiseBar.Maximum = double.IsNaN(poisemax) ? 1 : poisemax;
            poiseBar.Value = double.IsNaN(poise) ? 0 : poise > 0 ? poise : 0;
            poiseText.Text = $"Poise: {poise:F1} / {poisemax:F1}";

            //timer max is a bit annoying. you can try and 'find' it by observing the max and resetting on target switch.
            if (poisetimer < 0) { poiseTimerBar.Value = 0; poiseTimerBar.Maximum = 1; } //make sure not to lower maximum below old value
            if (poisetimer > poiseTimerBar.Maximum) { poiseTimerBar.Maximum = poisetimer; }
            var timeValToSet = poisetimer < 0 ? 0 : poisetimer > poiseTimerBar.Maximum ? poiseTimerBar.Maximum : poisetimer;
            poiseTimerBar.Value = timeValToSet;
            poiseTimerText.Text = $"Poise reset time: {poisetimer:F1}";

            if (resistsPanel.Visibility == Visibility.Visible)
            {
                var resistNames = new List<string>() { "Poison", "Rot", "Bleed", "Blight", "Frost", "Sleep", "Madness" };
                var resistBars = new List<ProgressBar>() { poisonBar, rotBar, bleedBar, blightBar, frostBar, sleepBar, madBar };
                var resistText = new List<TextBlock>() { poisonText, rotText, bleedText, blightText, frostText, sleepText, madText };

                for (int i = 0; i < resistNames.Count; i++)
                {
                    var statInd = ERProcess.TargetInfo.POISON + i * 2;
                    var statIndMax = statInd + 1;
                    var statAmount = _process.getSetTargetInfo(statInd);
                    var statMax = _process.getSetTargetInfo(statIndMax);
                    var statBar = resistBars[i];
                    var statText = resistText[i];
                    var statName = resistNames[i];

                    if (statBar.Value > statMax) { statBar.Value = 0; }
                    statBar.Maximum = statMax;
                    statBar.Value = statAmount > 0 ? statAmount : 0;
                    statText.Text = $"{statName}: {(int)statAmount} / {(int)statMax}";
                }
            }
            if (defensesPanel.Visibility == Visibility.Visible)
            {
                var slashDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.SLASH)) * 100);
                slashDefense = (int)Math.Round(slashDefense / 5.0) * 5;
                var strikeDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.STRIKE)) * 100);
                strikeDefense = (int)Math.Round(strikeDefense / 5.0) * 5;
                var pierceDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.PIERCE)) * 100);
                pierceDefense = (int)Math.Round(pierceDefense / 5.0) * 5;
                var standardDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.STANDARD)) * 100);
                standardDefense = (int)Math.Round(standardDefense / 5.0) * 5;

                var magicDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.MAGIC)) * 100);
                magicDefense = (int)Math.Round(magicDefense / 5.0) * 5;
                var fireDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.FIRE)) * 100);
                fireDefense = (int)Math.Round(fireDefense / 5.0) * 5;
                var lightningDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.LIGHTNING)) * 100);
                lightningDefense = (int)Math.Round(lightningDefense / 5.0) * 5;
                var holyDefense = (int)((1.0 - _process.getTargetDefenses(ERProcess.TargetInfo.HOLY)) * 100);
                holyDefense = (int)Math.Round(holyDefense / 5.0) * 5;

                slashVal.Text = slashDefense + "%";
                strikeVal.Text = strikeDefense + "%";
                pierceVal.Text = pierceDefense + "%";
                standardVal.Text = standardDefense + "%";

                magicVal.Text = magicDefense + "%";
                fireVal.Text = fireDefense + "%";
                lightningVal.Text = lightningDefense + "%";
                holyVal.Text = holyDefense + "%";
            }
        }

        void updateMovement()
        {
#if DEBUG
            if (!_process.isGameLoaded()) { Console.WriteLine("Game not loaded"); }
            //Utils.debugWrite(_process.getSetFreeCamCoords().ToString());
            {
                var mapCoords = _process.getMapCoords();
                if (!mapCoords.Equals(lastMapPos))
                {
                    lastMapPos = mapCoords;
                    Utils.debugWrite("Local: " + _process.getSetLocalCoords().ToString() + " Map: " + mapCoords.ToString() + " " + TeleportHelper.mapIDString(mapCoords.Item5) + " World: " + TeleportHelper.getWorldMapCoords(mapCoords));
                }
            }
            /*if (_process.isRiding())
            {
                var torCoords = _process.getSetTorrentLocalCoords();
                Utils.debugWrite(torCoords.ToString());
            }*/
#endif

            if (_noClipActive)
            {
                var noClipPos = _process.getSetFreeCamCoords();
                noClipPos.Item2 += 0.5f; //player slightly above camera
                _process.getSetLocalCoords(noClipPos);
            }

            var pos = _process.getSetLocalCoords();
            if (positionPanel.Visibility == Visibility.Visible)
            {
                var isRiding = _process.isRiding() ? "R" : "";
                var posPlayer = _process.getSetPlayerLocalCoords();
                var posTorrent = _process.getSetTorrentLocalCoords();
                var mapCoords = _process.getMapCoords();
                //localPos.Text = $"Local: [{pos.Item1:F2} {pos.Item2:F2} {pos.Item3:F2}]"; //switches between player and torrent
                localPos.Text = $"P: [{posPlayer.Item1:F2} {posPlayer.Item2:F2} {posPlayer.Item3:F2}] T: [{posTorrent.Item1:F2} {posTorrent.Item2:F2} {posTorrent.Item3:F2}] {isRiding}";
                var mapRotDeg = mapCoords.Item4 * 180 / Math.PI;
                var mapIDstr = TeleportHelper.mapIDString(mapCoords.Item5);
                mapPos.Text = $"Map: [{mapCoords.Item1:F2} {mapCoords.Item2:F2} {mapCoords.Item3:F2}] rotation: [{mapRotDeg:F1}°]";
                mapID.Text = $"Map ID: [{mapIDstr}]";
                var globalCoords = TeleportHelper.getWorldMapCoords(mapCoords);
                string dimension = "?";
                if (TeleportHelper.mapAreaIsMainWorld(globalCoords.Item4 << 24)) { dimension = "MAIN"; }
                else if (TeleportHelper.mapAreaIsDLC(globalCoords.Item4 << 24)) { dimension = "DLC"; }
                globalPos.Text = $"Global: [{dimension} {globalCoords.Item1:F2} {globalCoords.Item2:F2} {globalCoords.Item3:F2}]";
            }

            //track moving direction for the purpose of 'yeeting' 'forward'
            if (recentlyYeetedCounter > 0) { recentlyYeetedCounter--; return; }//only track regular movement
            var diff = (pos.Item1 - lastPos.Item1, pos.Item2 - lastPos.Item2, pos.Item3 - lastPos.Item3);
            lastPos = pos;
            var diffMag = (float)Math.Sqrt(diff.Item1 * diff.Item1 + diff.Item2 * diff.Item2 + diff.Item3 * diff.Item3);
            if (diffMag < 0.1 || diffMag > 5) { return; } //ignore tiny or huge changes
            var scale = YEET_AMOUNT / diffMag; //normalise
            var diffNormalised = (diff.Item1 * scale, diff.Item2 * scale, diff.Item3 * scale);
            var lpfA = 0.5f;
            diffNormalisedLpf = (diffNormalised.Item1 * lpfA + diffNormalisedLpf.Item1 * (1 - lpfA),
                diffNormalised.Item2 * lpfA + diffNormalisedLpf.Item2 * (1 - lpfA),
                diffNormalised.Item3 * lpfA + diffNormalisedLpf.Item3 * (1 - lpfA));
            //Console.WriteLine($"{diff} {diffMag} {diffNormalised} {diffNormalisedLpf}");
        }

        private void _timer_Tick(object sender, EventArgs e)
        {
            var good = _process?.weGood ?? false;
            dockPanel.IsEnabled = good;
            Title = good ? _normalTitle : "F?";

            if (!good) { return; }
            if (_hooked)
            {
                try
                {
                    updateTargetInfo();
                }
                catch { }
            }

            updateMovement();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Dispose(true);
        }

        private void colMeshAOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.COL_MESH_A);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_MAP);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_TREES);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_ROCKS);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_GRASS);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_DISTANT_MAP);
        }

        private void colMeshAOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.COL_MESH_A);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_MAP);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_TREES);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_ROCKS);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_GRASS);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_DISTANT_MAP);
        }

        //code is gonna get repetitive. i'm sorry. i never planned this many features.
        //TODO: simplify code somehow

        private void charMeshOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.CHARACTER_MESH);
        }

        private void charMeshOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.CHARACTER_MESH);
        }

        private void charModelHideOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_CHARACTER);
        }

        private void charModelHideOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_CHARACTER);
        }

        private void hitboxOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.HITBOX_VIEW_A);
        }

        private void hitboxOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.HITBOX_VIEW_A);
        }

        private void hitboxBOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.HITBOX_VIEW_B);
        }

        private void hitboxBOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.HITBOX_VIEW_B);
        }

        private void noDeathOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.NO_DEATH);
        }
        private void noDeathOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.NO_DEATH);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_process != null)
                    {
                        _process.Dispose();
                        _process = null;
                    }
                    if (_timer != null)
                    {
                        _timer.Stop();
                        _timer = null;
                    }
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void doQuitout(object sender, RoutedEventArgs e)
        {
            _process.enableOpt(ERProcess.DebugOpts.INSTANT_QUITOUT);
        }

        private void oneHPOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.ONE_HP);
        }

        private void oneHPOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.ONE_HP);
        }

        private void maxHPOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.MAX_HP);
        }

        private void maxHPOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.MAX_HP);
        }

        private void colMeshBOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.COL_MESH_B);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_MAP);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_TREES);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_ROCKS);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_GRASS);
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_DISTANT_MAP);
        }

        private void colMeshBOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.COL_MESH_B);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_MAP);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_TREES);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_ROCKS);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_GRASS);
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_DISTANT_MAP);
        }

        private void installTargetHook(object sender, RoutedEventArgs e)
        {
            (sender as Button).IsEnabled = false;
            if (!_process.installTargetHook())
            {
                MessageBox.Show("Could not install hook. This could be because a Cheat Engine table has already installed its own hook. Restart the game and try again.", "Sadge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _hooked = true;
            targetPanel.Opacity = 1;
            targetPanel.Visibility = Visibility.Visible;
            targetPanel.IsEnabled = true;
        }

        private void targetHpFreezeOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.TARGET_HP);
        }

        private void targetHpFreezeOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.TARGET_HP);
        }

        private void killTarget(object sender, RoutedEventArgs e)
        {
            _process.getSetTargetInfo(ERProcess.TargetInfo.HP, 0);
        }

        private void doYeet(object sender, RoutedEventArgs e)
        {
            var amt = YEET_AMOUNT;
            if (Keyboard.IsKeyDown(Key.LeftCtrl)) { amt *= 10; }
            if (Keyboard.IsKeyDown(Key.LeftShift)) { amt *= 10; }
            if (Keyboard.IsKeyDown(Key.LeftAlt)) { amt *= 100; }
            var pos = _process.getSetLocalCoords();
            var str = "";
            if (sender is string) { str = (string)sender; }
            if (sender is Button) { str = (string)((Button)sender).Content; }
            switch (str)
            {
                case "Forward":
                    {
                        pos.Item1 += amt * diffNormalisedLpf.Item1;
                        pos.Item2 += amt * diffNormalisedLpf.Item2;// + 0.2f; //consider, to go a bit up
                        pos.Item3 += amt * diffNormalisedLpf.Item3;
                        break;
                    }
                case "+X":
                    pos.Item1 += amt;
                    break;
                case "-X":
                    pos.Item1 -= amt;
                    break;
                case "+Y":
                    pos.Item2 += amt;
                    break;
                case "-Y":
                    pos.Item2 -= amt;
                    break;
                case "+Z":
                    pos.Item3 += amt;
                    break;
                case "-Z":
                    pos.Item3 -= amt;
                    break;
            }
            _process.getSetLocalCoords(pos);
            recentlyYeetedCounter = 5;
        }

        private void savePos(object sender, RoutedEventArgs e)
        {
            savedMapPos = _process.getMapCoords();
            restorePosButton.IsEnabled = true;
        }

        bool inWarp = false;

        void doGlobalTP((float, float, float, float, uint) pos)
        {//TODO: move this into erprocess
            if (inWarp) { return; } //block trying to warp multiple times at once as this can break the game
            inWarp = true;
            var mapCoordsNow = _process.getMapCoords();
            if (pos.Item5 == mapCoordsNow.Item5)
            {//same map region. don't attempt warp.
                _process.teleportToGlobal(pos);
                inWarp = false;
                return;
            }

            //you can die from long range TP
            var noDeathState = chkPlayerNoDeath.IsChecked;
            chkPlayerNoDeath.IsChecked = true;

            var noGravState = chkPlayerNoGrav.IsChecked;
            chkPlayerNoGrav.IsChecked = true;

            var t = new Thread(() =>
            {
                Thread.Sleep(250); //wait for freeze to enable

                var ret = _process.teleportToGlobal(pos, 0.5f, warpIfNeeded: true);
                if (ret == 0)
                {//just ported
                    Thread.Sleep(5000);
                    _process.teleportToGlobal(pos, 0.5f);
                }
                else if (ret == 1)
                {//warped. TODO: identify if we've fully loaded in, or at least mostly.
                    Thread.Sleep(5000);
                }
                Dispatcher.Invoke(() =>
                {
                    chkPlayerNoGrav.IsChecked = noGravState;
                    chkPlayerNoDeath.IsChecked = noDeathState;
                    inWarp = false;
                });
            });
            t.Start();
        }

        private void restorePos(object sender, RoutedEventArgs e)
        {
            if (savedMapPos.HasValue)
            {
                doGlobalTP(savedMapPos.Value);
            }
        }

        private void noAIOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_AI);
        }

        private void noAIOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_AI);
        }

        private void noStamOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.NO_STAM);
        }

        private void noStamOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.NO_STAM);
        }

        private void noFPOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.NO_FP);
        }

        private void noFPOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.NO_FP);
        }

        void setHPStr(string str)
        {
            if (string.IsNullOrEmpty(str)) { return; }
            int setAmount = 0;
            if (str.EndsWith("%"))
            {
                str = str.Substring(0, str.Length - 1);
                if (!float.TryParse(str, out var perc)) { return; }
                var maxHP = _process.getSetTargetInfo(ERProcess.TargetInfo.MAX_HP);
                setAmount = (int)(maxHP * perc / 100.0f);
            }
            else
            {
                if (!int.TryParse(str, out var hp)) { return; }
                setAmount = hp;
            }
            _process.getSetTargetInfo(ERProcess.TargetInfo.HP, setAmount);
        }

        private void setHP(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (null == button) { return; }
            var str = button.Content as string;
            setHPStr(str);
        }

        private void setHPCustom(object sender, RoutedEventArgs e)
        {
            var amount = Microsoft.VisualBasic.Interaction.InputBox("Enter hp value (or put % for percentage)", "HP", "420");
            setHPStr(amount);
        }

        private void addSouls(object sender, RoutedEventArgs e)
        {
            int amt = 1000000;
            if (Keyboard.IsKeyDown(Key.LeftCtrl)) { amt = 999999999; }
            if (Keyboard.IsKeyDown(Key.LeftShift)) { amt = 999999999; }
            _process.addRunes(amt);
        }

        private void hotkeySetup(object sender, RoutedEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                clearRegisteredHotkeys();
                if (!parseHotkeys())
                {
                    MessageBox.Show("Failed to parse hotkey file.");
                }
                else
                {
                    MessageBox.Show(registeredHotkeys.Count + " hotkeys registered.");
                }
            }
            else if (Keyboard.IsKeyDown(Key.LeftShift))
            {
                var psi = new ProcessStartInfo(System.IO.Path.GetDirectoryName(getHotkeyFileAppData()));
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            else if (Keyboard.IsKeyDown(Key.LeftAlt))
            {
                var res = MessageBox.Show("Reset hotkey file?", "Reset hotkeys", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    backupHotkeyFile();
                    generateDefaultHotkeyFile();
                    var psi = new ProcessStartInfo(hotkeyFile());
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
            }
            else
            {
                if (!File.Exists(hotkeyFile())) { generateDefaultHotkeyFile(); }
                var psi = new ProcessStartInfo(hotkeyFile());
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
        }

        private void goToWebsite(object sender, RoutedEventArgs e)
        {
            var psi = new ProcessStartInfo(websiteUrl);
            Process.Start(psi);
        }

        void notifyOfUpdate()
        {
            websiteButton.Content = "Update Available";
            websiteButton.Foreground = Brushes.Red;
            websiteButton.FontWeight = FontWeights.Bold;
        }

        public void doUpdateCheck(bool force = false)
        {
            var checkFile = Utils.getFnameInAppdata("LastUpdateCheck", "ERTool");
            var lastCheckDate = Utils.getFileDate(checkFile);
            var sinceLastCheck = DateTime.Now - lastCheckDate;
            Utils.debugWrite($"Last check was {sinceLastCheck.TotalDays} days ago");
            if (force || sinceLastCheck.TotalDays >= 1)
            {
                Utils.debugWrite("Checking for update...");
                var runningAssembly = Assembly.GetEntryAssembly().GetName();
                var userAgent = runningAssembly.Name + " v" + runningAssembly.Version;
                //this will block until finished which could be a long time
                int versionCheck = Utils.checkVerAgainstURL(updateCheckUrl, userAgent, runningAssembly.Version.ToString());

                Utils.debugWrite("Version check result: " + versionCheck);

                Utils.setFileDate(checkFile);

                if (versionCheck == 1)
                {
                    Dispatcher.Invoke(notifyOfUpdate);
                }
            }
        }

        private void enableUpdateCheck(object sender, RoutedEventArgs e)
        {
            if (!updateCheckStartupCheckDone) { return; }
            Utils.removeFile(getUpdCheckFile());
            doUpdateCheck(true); //runs on the gui thread - should succeed or fail quickly
        }

        private void disableUpdateCheck(object sender, RoutedEventArgs e)
        {
            Utils.getFileDate(getUpdCheckFile()); //lazy way to create a file safely
        }

        void maybeDoUpdateCheck()
        {
            if (!File.Exists(getUpdCheckFile()))
            {
                chkAutoUpdate.IsChecked = true;
                var t = new Thread(()=>
                {
                    try
                    {
                        doUpdateCheck();
                    }
                    catch (Exception ex) { Utils.debugWrite(ex.ToString()); }
                });
                t.IsBackground = true;
                t.Start();
            }
            updateCheckStartupCheckDone = true;
        }

        private void topDebugOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.TOP_DEBUG_MENU);
        }

        private void topDebugOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.TOP_DEBUG_MENU);
        }

        private void noGravOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.NO_GRAVITY);
        }

        private void noGravOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.NO_GRAVITY);
        }

        private void changeMeshColours(object sender, RoutedEventArgs e)
        {
            _process.cycleMeshColours();
        }

        private void poiseViewOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.POISE_VIEW);
        }

        private void poiseViewOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.POISE_VIEW);
        }

        private void stayOnTop(object sender, RoutedEventArgs e)
        {
            Topmost = true;
        }

        private void dontStayOnTop(object sender, RoutedEventArgs e)
        {
            Topmost = false;
        }

        private void noMapColOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.NO_MAP_COLLISION);
        }

        private void nomapColOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.NO_MAP_COLLISION);
        }

        private void torNoDeathOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.TORRENT_NO_DEATH);
        }

        private void torNoDeathOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.TORRENT_NO_DEATH);
        }

        private void torNoGravOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.TORRENT_NO_GRAV);
        }

        private void torNoGravOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.TORRENT_NO_GRAV);
        }

        private void torNoMapColOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.TORRENT_NO_MAP_COLL);
        }

        private void torNomapColOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.TORRENT_NO_MAP_COLL);
        }

        private void eventDrawOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.EVENT_DRAW);
        }

        private void eventDrawOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.EVENT_DRAW);
        }

        private void eventStopOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.EVENT_STOP);
        }

        private void eventStopOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.EVENT_STOP);
        }

        
        private void freeCamOn(object sender, RoutedEventArgs e)
        {
            if (_freeCamFirstActivation || Keyboard.IsKeyDown(Key.LeftShift))
            {
                _freeCamFirstActivation = false;
                moveCamToPlayer(null, null);
            }
            _process.freezeOn(ERProcess.DebugOpts.FREE_CAM);
        }

        private void freeCamOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.FREE_CAM);
        }

        
        private void toggleStatsFull(object sender, RoutedEventArgs e)
        {
            if (!isCompact)
            {
                setCompact();
            }
            else
            {
                setFull();
            }
        }
        void setCompact()
        {
            chkStayOnTop.IsChecked = true;
            if (targetHookButton.IsEnabled) { installTargetHook(targetHookButton, null); }

            mainPanel.Visibility = Visibility.Collapsed;

            freezeHPPanel.Visibility = Visibility.Collapsed;
            quitoutButton.Visibility = Visibility.Collapsed;
            updatePanel.Visibility = Visibility.Collapsed;

            isCompact = true;
        }
            
        void setFull()
        {
            mainPanel.Visibility = Visibility.Visible;

            freezeHPPanel.Visibility = Visibility.Visible;
            quitoutButton.Visibility = Visibility.Visible;
            updatePanel.Visibility = Visibility.Visible;

            isCompact = false;
        }

        private void toggleResists(object sender, RoutedEventArgs e)
        {
            resistsPanel.Visibility = resistsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void toggleDefenses(object sender, RoutedEventArgs e)
        {
            defensesPanel.Visibility = defensesPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void savePosDB(object sender, RoutedEventArgs e)
        {
            var pos = _process.getMapCoords();
            var name = Microsoft.VisualBasic.Interaction.InputBox("Enter a name for this location", "Location name", "Somewhere");
            if (!string.IsNullOrEmpty(name))
            {
                var str = TeleportHelper.mapCoordsToString(pos) + "," + name;
                try
                {
                    File.AppendAllText(posDbFile(), str + Environment.NewLine);
                }
                catch { }
            }
        }

        private void restorePosDB(object sender, RoutedEventArgs e)
        {
            var dbLocations = File.ReadAllLines(posDbFile());
            var locations = new List<TeleportLocation>();
            for (int i = 0; i < dbLocations.Length; i++) 
            {
                locations.Add(new TeleportLocation(dbLocations[i]));   
            }

            var sel = new Selection(locations.ToList<object>(), (x) => { if (x != null) { doGlobalTP((x as TeleportLocation).getCoords()); } }, "Choose a location: ");
            sel.Owner = this;
            sel.Show();
        }

        private void combatMapOn(object sender, RoutedEventArgs e)
        {
            _process.doCombatMapPatch();
        }

        private void combatMapOff(object sender, RoutedEventArgs e)
        {
            _process.undoCombatMapPatch();
        }

        private void noGoodsOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.NO_GOODS);
            _process.freezeOn(ERProcess.DebugOpts.NO_ARROWS);
        }

        private void noGoodsOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.NO_GOODS);
            _process.offAndUnFreeze(ERProcess.DebugOpts.NO_ARROWS);
        }

        private void moveCamToPlayer(object sender, RoutedEventArgs e)
        {
            var player = _process.getSetLocalCoords();
            player.Item2 += 1.7f; //offset to roughly player head
            _process.getSetFreeCamCoords(player);
        }

        private void movePlayerToCam(object sender, RoutedEventArgs e)
        {
            var cam = _process.getSetFreeCamCoords();
            _process.getSetLocalCoords(cam);
        }

        private void noClipOn(object sender, RoutedEventArgs e)
        {
            moveCamToPlayer(null, null);
            _playerNoDeathStateWas = chkPlayerNoDeath.IsChecked ?? false;
            _torNoDeathStateWas = chkTorNoDeath.IsChecked ?? false;
            chkPlayerNoDeath.IsChecked = true;
            chkPlayerNoGrav.IsChecked = true;
            chkTorNoDeath.IsChecked = true;
            chkTorNoGrav.IsChecked = true;
            Thread.Sleep(100); //ugh. makes player less likely to go into a falling animation right away. need a better fix.
            chkPlayerNoMapCol.IsChecked = true;
            chkTorNoMapCol.IsChecked = true;
            chkFreeCam.IsChecked = true;
            _noClipActive = true;
        }

        private void noClipOff(object sender, RoutedEventArgs e)
        {
            _noClipActive = false;
            chkFreeCam.IsChecked = false;
            chkPlayerNoMapCol.IsChecked = false;
            chkTorNoMapCol.IsChecked = false;
            chkPlayerNoGrav.IsChecked = false;
            chkTorNoGrav.IsChecked = false;
            chkTorNoDeath.IsChecked = false;
            chkPlayerNoDeath.IsChecked = _playerNoDeathStateWas;
            chkTorNoDeath.IsChecked = _torNoDeathStateWas;
        }

        private void freeCamControlOn(object sender, RoutedEventArgs e)
        {
            _process.doFreeCamPlayerControlPatch();
        }

        private void freeCamControlOff(object sender, RoutedEventArgs e)
        {
            _process.undoFreeCamPlayerControlPatch();
        }

        private void repeatActionOn(object sender, RoutedEventArgs e)
        {
            _process.setEnemyRepeatActionPatch(true);
        }

        private void repeatActionOff(object sender, RoutedEventArgs e)
        {
            _process.setEnemyRepeatActionPatch(false);
        }

        private void instantDeath(object sender, RoutedEventArgs e)
        {
            _process.getSetPlayerHP(0);
        }

        private void steamInputEnumDisableOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_STEAM_INPUT_ENUM);
        }

        private void steamInputEnumDisableOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_STEAM_INPUT_ENUM);
        }

        private void disableAchieveOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.DISABLE_STEAM_ACHIVEMENTS);
        }

        private void disableAchieveOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.DISABLE_STEAM_ACHIVEMENTS);
        }

        private void runeArcOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.RUNE_ARC);
        }

        private void runArcOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.RUNE_ARC);
        }

        private void setGameSpeed(object sender, RoutedEventArgs e)
        {
            var existing = _process.getSetGameSpeed();
            var newVal = Microsoft.VisualBasic.Interaction.InputBox("Enter game speed multiplier", "Game Speed", existing.ToString());
            if (string.IsNullOrEmpty(newVal)) { return; }
            if (float.TryParse(newVal, out var newValFlt))
            {
                _process.getSetGameSpeed(newValFlt);
            }
        }

        private void setTargetFPS(object sender, RoutedEventArgs e)
        {
            var existing = 1.0f / _process.getSetFrameTimeTarget();
            var newVal = Microsoft.VisualBasic.Interaction.InputBox("Enter target FPS (windowed/borderless)", "Target FPS", existing.ToString());
            if (string.IsNullOrEmpty(newVal)) { return; }
            if (float.TryParse(newVal, out var newValFlt))
            {
                newValFlt = 1.0f / newValFlt;
                _process.getSetFrameTimeTarget(newValFlt);
            }
        }

        private void toggleCoords(object sender, RoutedEventArgs e)
        {
            positionPanel.Visibility = positionPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void freeUpgOn(object sender, RoutedEventArgs e)
        {
            _process.setFreeUpgrade(true);
        }

        private void freeUpgOff(object sender, RoutedEventArgs e)
        {
            _process.setFreeUpgrade(false);
        }

        private void soundViewOn(object sender, RoutedEventArgs e)
        {
            _process.setSoundView(true);
        }

        private void soundViewOff(object sender, RoutedEventArgs e)
        {
            _process.setSoundView(false);
        }

        private void targetingViewOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.TARGETING_VIEW);
        }

        private void targetingViewOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.TARGETING_VIEW);
        }

        private void editStats(object sender, RoutedEventArgs e)
        {
            var stats = _process.getSetPlayerStats();
            var editor = new StatsEditor(stats, (x) =>
            {
                _process.getSetPlayerStats(x);
            });
            editor.Owner = this;
            editor.Show();
        }

        private void spawnItem(object sender, RoutedEventArgs e)
        {
            var itemSpawn = new ItemSpawn(_process);
            itemSpawn.Owner = this;
            itemSpawn.Show();
        }

        private void torrentAnywhereOn(object sender, RoutedEventArgs e)
        {
            _process.setTorrentAnywherePatch(true);
        }

        private void torrentAnywhereOff(object sender, RoutedEventArgs e)
        {
            _process.setTorrentAnywherePatch(false);
        }

        private void noDeathAllOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.ALL_CHR_NO_DEATH);
        }

        private void noDeathAllOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.ALL_CHR_NO_DEATH);
        }

        int? lastSetHP = null;

        private void btnSetPlayerHP_Click(object sender, RoutedEventArgs e)
        {
            var existing = _process.getSetPlayerHP();
            var newVal = Microsoft.VisualBasic.Interaction.InputBox("Enter HP", "Set Player HP", existing.ToString());
            if (string.IsNullOrEmpty(newVal)) { return; }
            if (int.TryParse(newVal, out var newValInt))
            {
                _process.getSetPlayerHP(newValInt);
                lastSetHP = newValInt;
            }
        }

        private void oneShotOn(object sender, RoutedEventArgs e)
        {
            _process.freezeOn(ERProcess.DebugOpts.ONE_SHOT);
        }

        private void oneShotOff(object sender, RoutedEventArgs e)
        {
            _process.offAndUnFreeze(ERProcess.DebugOpts.ONE_SHOT);
        }

        private void muteMusic(object sender, RoutedEventArgs e)
        {
            _process.doMusicMutePatch(true);
        }

        private void unmuteMusic(object sender, RoutedEventArgs e)
        {
            _process.doMusicMutePatch(false);
        }

        private void openMenu(object sender, RoutedEventArgs e)
        {
            var sel = new Selection(_process.MENUS.ToList<object>(), x => _process.openMenuByName(x as string));
            sel.Owner = this;
            sel.Show();
        }

        private void flags(object sender, RoutedEventArgs e)
        {
            var selections = new List<object>();
            foreach (var k in FlagDB.data.Keys)
            {
                if (k.Contains("DLC") && !_process.exeSupportsDlc()) { continue; } //TODO: check for flags indicating that the player has the DLC
                selections.Add(k);
            }
            const string specificFlag = "Specific flag";
            const string extraFlagsStr = "Extra Flags";
            selections.Add(specificFlag);
            selections.Add(extraFlagsStr);
            var sel = new Selection(selections, x =>
            {
                var str = x as string;
                if (null == str) { return; }
                if (specificFlag == str) { getSetFlag(); }
                else if (extraFlagsStr == str) { extraFlags(); }
                else if (FlagDB.data.TryGetValue(str, out var data))
                {
                    if (str.Contains("Bosses"))
                    {
                        var sel2 = new Selection(data.Select(y => y.Item1).ToList<object>(), z =>
                        {
                            var bossID = data.Where(w => w.Item1 == z as string).FirstOrDefault();
                            if (bossID.Item2 != 0) { _process.getSetEventFlag(bossID.Item2, false); }
                        }, "Resurrect Boss");
                        sel2.Owner = this;
                        sel2.Show();
                    }
                    else
                    {
                        Console.WriteLine("Setting all flags for: " + str);
                        foreach (var row in data)
                        {
                            _process.getSetEventFlag(row.Item2, true);
                        }
                    }
                }
            });
            sel.Owner = this;
            sel.Show();
        }
        void getSetFlag(int? preSetFlag = null)
        {
            var flagNum = Microsoft.VisualBasic.Interaction.InputBox("Enter flag number", "Flag", preSetFlag?.ToString() ?? "");
            if (!int.TryParse(flagNum, out var flagNumInt)) { return; }
            var val = _process.getSetEventFlag(flagNumInt);
            var flagVal = Microsoft.VisualBasic.Interaction.InputBox("Enter value", "Flag " + flagNumInt, val.ToString());
            if (!bool.TryParse(flagVal, out var flagValBool)) { return; }
            _process.getSetEventFlag(flagNumInt, flagValBool);
        }

        void extraFlags()
        {
            if (!File.Exists(extraFlagsFile()))
            {
                File.WriteAllText(extraFlagsFile(), "ID,Name" + Environment.NewLine + "70,DLC-Cleared NG+ Scaling");
            }
            var flagLines = File.ReadAllLines(extraFlagsFile());
            var flags = new List<ExtraFlag>();
            for (int i = 1; i < flagLines.Length; i++)
            {
                var flag = ExtraFlag.parse(flagLines[i]);
                if (flag != null && flag.id != -1)
                {
                    flag.state = _process.getSetEventFlag(flag.id);
                }
                flags.Add(flag);
            }
            var sel = new Selection(flags.ToList<object>(), x =>
            {
                var flag = x as ExtraFlag;
                if (null == flag) { return; }
                var val = _process.getSetEventFlag(flag.id);
                var flagVal = Microsoft.VisualBasic.Interaction.InputBox("Enter value", flag.name, val.ToString());
                if (!bool.TryParse(flagVal, out var flagValBool)) { return; }
                _process.getSetEventFlag(flag.id, flagValBool);
            }, "Select Flag");
            sel.Owner = this;
            sel.Show();
        }

        private void setClearCount(object sender, RoutedEventArgs e)
        {
            var cc = _process.getSetClearCount();

            string trigger = "Trigger NG+";
            string setCC = $"Set NG+ level (currently {cc})";

            var sel = new Selection(new List<object>() { trigger, setCC }, x =>
            {
                if (trigger.Equals(x))
                {
                    _process.triggerNGPlus();
                    return;
                }
                cc = _process.getSetClearCount();
                var ccNewStr = Microsoft.VisualBasic.Interaction.InputBox("Enter NG+ level (ClearCount): ", "NG+", cc.ToString());
                if (!int.TryParse(ccNewStr, out var ccNew)) { return; }
                _process.getSetClearCount(ccNew);
            });
            sel.Owner = this;
            sel.Show();
        }

        private void dockPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DockPanel dockPanel && dockPanel.Tag is StackPanel stackPanel)
            {
                stackPanel.Visibility = stackPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

                var textBox = dockPanel.Children.OfType<TextBlock>().FirstOrDefault();
                if (textBox != null)
                {
                    textBox.Text = stackPanel.Visibility == Visibility.Visible ? 
                                                            textBox.Text.Substring(0, textBox.Text.Length - 1) + "▼" : 
                                                            textBox.Text.Substring(0, textBox.Text.Length - 1) + "▲";
                }
            }
        }

        private void ToggleCollapse(object sender, RoutedEventArgs e)
        {
            var newVisibility = panelsCollapsed ? Visibility.Visible : Visibility.Collapsed;

            foreach (UIElement element in mainPanel.Children)
            {
                if (element is StackPanel stackPanel && stackPanel.Name != null && stackPanel.Visibility != newVisibility)
                {
                    stackPanel.Visibility = newVisibility;
                }
                if (element is DockPanel dockPanel)
                {
                    FixPanelArrows(dockPanel, newVisibility.ToString());
                }
            }

            panelsCollapsed = !panelsCollapsed;

            if (sender is Button button)
            {
                button.Content = newVisibility == Visibility.Visible ? "▼" : "▲";
            }
        }

        private void FixPanelArrows(DockPanel panel, string visibility)
        {
            var textBox = panel.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBox != null)
            {
                textBox.Text = visibility == Visibility.Visible.ToString() ?
                                                        textBox.Text.Substring(0, textBox.Text.Length - 1) + "▼" :
                                                        textBox.Text.Substring(0, textBox.Text.Length - 1) + "▲";
            }
        }

        private void RestorePanelVisibility(DockPanel dockPanel, StackPanel stackPanel, string panelVisibility)
        {
            if (panelVisibility != Visibility.Visible.ToString())
            {
                stackPanel.Visibility = Visibility.Collapsed;
                FixPanelArrows(dockPanel, panelVisibility);
            }
        }
    }
}
