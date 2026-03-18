using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SlayTheSpire2.LAN.Multiplayer.Helpers;
using SlayTheSpire2.LAN.Multiplayer.Models;
using SlayTheSpire2.LAN.Multiplayer.Services;

namespace SlayTheSpire2.LAN.Multiplayer.Components
{
    internal class LanJoinFriendPanel : NinePatchRect
    {
        public NJoinFriendScreen? JoinFriendScreen { get; set; }

        private AddressLineEdit? _addressInput;
        private CancellationTokenSource? _discoveryCancellationTokenSource;
        private VBoxContainer? _discoveredRoomContainer;
        private Task<IReadOnlyList<LanDiscoveredRoomModel>>? _discoveryTask;
        private bool _initialized;
        private bool _isJoining;
        private Label? _statusLabel;

        public override void _Ready()
        {
            RuntimeTrace.Write("[LAN] JoinFriendPanel _Ready called.");
            EnsureInitialized();
        }

        public override void _Process(double delta)
        {
            if (!_initialized)
            {
                EnsureInitialized();
                return;
            }

            if (_discoveryTask == null || !_discoveryTask.IsCompleted)
                return;

            var discoveryTask = _discoveryTask;
            _discoveryTask = null;
            _discoveryCancellationTokenSource?.Dispose();
            _discoveryCancellationTokenSource = null;

            if (_isJoining || !GodotObject.IsInstanceValid(this))
                return;

            if (discoveryTask.IsCanceled)
                return;

            if (discoveryTask.IsFaulted)
            {
                RuntimeTrace.Write($"[LAN] Discovery task faulted: {discoveryTask.Exception}");
                SetStatus("扫描局域网房间失败，可手动输入 IP 连接");
                return;
            }

            HandleDiscoveredRooms(discoveryTask.Result);
        }

        public override void _ExitTree()
        {
            CancelDiscovery();
            _initialized = false;
        }

        public void EnsureInitialized()
        {
            if (_initialized)
                return;

            if (JoinFriendScreen == null || !GodotObject.IsInstanceValid(JoinFriendScreen))
                return;

            try
            {
                BuildPanelLayout();
                StartDiscovery();
                _initialized = true;
                RuntimeTrace.Write("[LAN] JoinFriend panel initialized.");
            }
            catch (Exception ex)
            {
                RuntimeTrace.Write($"[LAN] JoinFriend panel initialize failed: {ex}");
            }
        }

        private void BuildPanelLayout()
        {
            ClipContents = true;

            foreach (var child in GetChildren())
            {
                child.QueueFree();
            }

            var marginContainer = new MarginContainer();
            AddChild(marginContainer);
            marginContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            marginContainer.OffsetLeft = 12;
            marginContainer.OffsetTop = 12;
            marginContainer.OffsetRight = -12;
            marginContainer.OffsetBottom = -12;

            var contentContainer = new VBoxContainer();
            marginContainer.AddChild(contentContainer);
            contentContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            contentContainer.AddThemeConstantOverride("separation", 10);

            _statusLabel = CreateSectionLabel("正在扫描局域网房间...", 20);
            contentContainer.AddChild(_statusLabel);

            var roomScrollContainer = new ScrollContainer();
            contentContainer.AddChild(roomScrollContainer);
            roomScrollContainer.CustomMinimumSize = new Vector2(0, 180);
            roomScrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

            _discoveredRoomContainer = new VBoxContainer();
            roomScrollContainer.AddChild(_discoveredRoomContainer);
            _discoveredRoomContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            _discoveredRoomContainer.AddThemeConstantOverride("separation", 8);

            contentContainer.AddChild(CreateActionButton("RefreshRoomsButton", "刷新房间", StartDiscovery));

            contentContainer.AddChild(CreateSectionLabel("手动连接", 24));

            _addressInput = new AddressLineEdit
            {
                Name = "AddressInput",
                Text = SettingsService.Instance.SettingsModel.IPAddress,
                Alignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(0, 48),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _addressInput.AddThemeFontSizeOverride("font_size", 22);
            contentContainer.AddChild(_addressInput);

            contentContainer.AddChild(CreateActionButton("ManualJoinButton", "手动加入",
                () => TaskHelper.RunSafely(JoinManualAddressAsync())));
        }

        private void StartDiscovery()
        {
            if (_isJoining)
                return;

            CancelDiscovery();
            ClearDiscoveredRoomButtons();
            SetStatus("正在扫描局域网房间...");

            _discoveryCancellationTokenSource = new CancellationTokenSource();
            _discoveryTask = LanDiscoveryService.Instance.DiscoverRoomsAsync(
                LanDiscoveryProtocol.TimeoutMs,
                _discoveryCancellationTokenSource.Token);
        }

        private void CancelDiscovery()
        {
            _discoveryCancellationTokenSource?.Cancel();
            _discoveryCancellationTokenSource?.Dispose();
            _discoveryCancellationTokenSource = null;
            _discoveryTask = null;
        }

        private void HandleDiscoveredRooms(IReadOnlyList<LanDiscoveredRoomModel> discoveredRooms)
        {
            if (discoveredRooms.Count == 0)
            {
                SetStatus("未发现局域网房间，可手动输入 IP 连接");
                return;
            }

            if (discoveredRooms.Count == 1)
            {
                SetStatus($"发现 1 个房间，正在自动加入 {GetRoomDisplayText(discoveredRooms[0])}");
                JoinDiscoveredRoom(discoveredRooms[0]);
                return;
            }

            SetStatus($"发现 {discoveredRooms.Count} 个房间，请点击要加入的房间");

            foreach (var discoveredRoom in discoveredRooms)
            {
                var button = CreateActionButton(
                    $"JoinRoom_{discoveredRoom.HostAddress}_{discoveredRoom.HostPort}",
                    GetRoomDisplayText(discoveredRoom),
                    () => JoinDiscoveredRoom(discoveredRoom));
                _discoveredRoomContainer?.AddChild(button);
            }
        }

        private async Task JoinManualAddressAsync()
        {
            if (_addressInput == null)
                return;

            var addressInfo = _addressInput.GetAddressInfo();
            if (!addressInfo.IsValid || string.IsNullOrEmpty(addressInfo.Address))
            {
                SetStatus("请输入有效的局域网 IP 或 IP:端口");
                return;
            }

            var port = addressInfo.Port ?? 33771;
            await JoinAddressAsync(_addressInput.Text, addressInfo.Address, port, "正在连接手动输入的房间...");
        }

        private void JoinDiscoveredRoom(LanDiscoveredRoomModel discoveredRoom)
        {
            if (_addressInput != null)
                _addressInput.Text = $"{discoveredRoom.HostAddress}:{discoveredRoom.HostPort}";

            TaskHelper.RunSafely(JoinAddressAsync(
                $"{discoveredRoom.HostAddress}:{discoveredRoom.HostPort}",
                discoveredRoom.HostAddress,
                discoveredRoom.HostPort,
                $"正在连接 {GetRoomDisplayText(discoveredRoom)}"));
        }

        private async Task JoinAddressAsync(string persistedAddress, string hostAddress, ushort port, string statusText)
        {
            if (JoinFriendScreen == null || !GodotObject.IsInstanceValid(JoinFriendScreen))
                return;

            CancelDiscovery();
            _isJoining = true;
            SetStatus(statusText);

            try
            {
                SettingsService.Instance.SettingsModel.IPAddress = persistedAddress;
                SettingsService.Instance.WriteSettings();

                DisplayServer.WindowSetTitle("Slay the Spire 2 (LAN Client)");
                await JoinFriendScreen.JoinGameAsync(new ENetClientConnectionInitializer(
                    SettingsService.Instance.SettingsModel.NetId,
                    hostAddress,
                    port));
            }
            catch
            {
                SetStatus("连接失败，可刷新房间列表或手动输入 IP 重试");
                throw;
            }
            finally
            {
                _isJoining = false;
            }
        }

        private static Label CreateSectionLabel(string text, int fontSize)
        {
            var label = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            return label;
        }

        private static Button CreateActionButton(string name, string text, Action onPressed)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                CustomMinimumSize = new Vector2(0, 48),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            button.AddThemeFontSizeOverride("font_size", 22);
            button.Pressed += onPressed;
            return button;
        }

        private void ClearDiscoveredRoomButtons()
        {
            if (_discoveredRoomContainer == null)
                return;

            foreach (var child in _discoveredRoomContainer.GetChildren())
            {
                child.QueueFree();
            }
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.Text = text;
        }

        private static string GetRoomDisplayText(LanDiscoveredRoomModel discoveredRoom)
        {
            var hostName = string.IsNullOrWhiteSpace(discoveredRoom.HostName) ? "未知主机" : discoveredRoom.HostName;
            var gameMode = string.IsNullOrWhiteSpace(discoveredRoom.GameMode) ? "未知模式" : discoveredRoom.GameMode;
            return $"{hostName} | {gameMode} | {discoveredRoom.HostAddress}:{discoveredRoom.HostPort}";
        }
    }
}
