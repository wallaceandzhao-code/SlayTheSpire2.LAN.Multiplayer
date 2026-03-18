using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
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
        private bool _isJoining;
        private MegaLabel? _statusLabel;

        /// <summary>
        /// 初始化局域网加入面板，并在进入页面后立即触发一次房间扫描。
        /// </summary>
        public override void _Ready()
        {
            if (JoinFriendScreen == null)
                return;

            BuildPanelLayout();
            StartDiscovery();
        }

        /// <summary>
        /// 在每帧里轮询 discovery 任务，确保网络扫描在后台执行、UI 更新在主线程完成。
        /// </summary>
        public override void _Process(double delta)
        {
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
                SetStatus("扫描局域网房间失败，可手动输入 IP 连接");
                return;
            }

            HandleDiscoveredRooms(discoveryTask.Result);
        }

        /// <summary>
        /// 离开页面时终止 discovery 扫描，避免后台任务继续占用资源。
        /// </summary>
        public override void _ExitTree()
        {
            CancelDiscovery();
        }

        /// <summary>
        /// 构造局域网加入面板的控件布局，保留房间扫描和手动输入两个入口。
        /// </summary>
        private void BuildPanelLayout()
        {
            var contentContainer = new VBoxContainer();
            AddChild(contentContainer);

            contentContainer.Alignment = BoxContainer.AlignmentMode.Begin;
            contentContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            contentContainer.AddThemeConstantOverride("separation", 16);

            _statusLabel = CreateSectionLabel("正在扫描局域网房间...");
            if (_statusLabel != null)
            {
                contentContainer.AddChild(_statusLabel);
            }

            var roomScrollContainer = new ScrollContainer();
            contentContainer.AddChild(roomScrollContainer);

            roomScrollContainer.CustomMinimumSize = new Vector2(300, 220);
            roomScrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

            _discoveredRoomContainer = new VBoxContainer();
            roomScrollContainer.AddChild(_discoveredRoomContainer);

            _discoveredRoomContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            _discoveredRoomContainer.AddThemeConstantOverride("separation", 12);

            if (CreateActionButton("RefreshRoomsButton", "刷新房间", StartDiscovery) is { } refreshButton)
            {
                contentContainer.AddChild(refreshButton);
            }

            if (CreateSectionLabel("手动连接") is { } manualTitleLabel)
            {
                contentContainer.AddChild(manualTitleLabel);
            }

            _addressInput = new AddressLineEdit { Name = "AddressInput" };
            contentContainer.AddChild(_addressInput);

            _addressInput.Text = SettingsService.Instance.SettingsModel.IPAddress;
            _addressInput.Alignment = HorizontalAlignment.Center;
            _addressInput.CustomMinimumSize = new Vector2(300, 50);
            _addressInput.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

            if (CreateActionButton("ManualJoinButton", "手动加入",
                    () => TaskHelper.RunSafely(JoinManualAddressAsync())) is { } manualJoinButton)
            {
                contentContainer.AddChild(manualJoinButton);
            }
        }

        /// <summary>
        /// 启动一次新的局域网扫描；如果上一次扫描尚未结束，会先取消旧任务。
        /// </summary>
        private void StartDiscovery()
        {
            if (_isJoining)
                return;

            CancelDiscovery();
            ClearDiscoveredRoomButtons();
            SetStatus("正在扫描局域网房间...");

            _discoveryCancellationTokenSource = new CancellationTokenSource();
            _discoveryTask = LanDiscoveryService.Instance.DiscoverRoomsAsync(LanDiscoveryProtocol.TimeoutMs,
                _discoveryCancellationTokenSource.Token);
        }

        /// <summary>
        /// 取消当前正在进行的房间扫描任务。
        /// </summary>
        private void CancelDiscovery()
        {
            _discoveryCancellationTokenSource?.Cancel();
            _discoveryCancellationTokenSource?.Dispose();
            _discoveryCancellationTokenSource = null;
            _discoveryTask = null;
        }

        /// <summary>
        /// 根据扫描结果切换交互分支：单房间自动加入，多房间展示列表，无房间回退手动输入。
        /// </summary>
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

            // 多房间时为每个房间生成一个独立加入按钮，避免再额外做“选择后确认”的交互。
            foreach (var discoveredRoom in discoveredRooms)
            {
                if (CreateActionButton($"JoinRoom_{discoveredRoom.HostAddress}_{discoveredRoom.HostPort}",
                        GetRoomDisplayText(discoveredRoom), () => JoinDiscoveredRoom(discoveredRoom)) is
                    { } roomButton)
                {
                    _discoveredRoomContainer?.AddChild(roomButton);
                }
            }
        }

        /// <summary>
        /// 处理手动输入地址的加入逻辑，继续复用原有 ENet 加入流程。
        /// </summary>
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

        /// <summary>
        /// 处理 discovery 找到的房间加入逻辑，并同步更新输入框里的地址显示。
        /// </summary>
        private void JoinDiscoveredRoom(LanDiscoveredRoomModel discoveredRoom)
        {
            if (_addressInput != null)
            {
                _addressInput.Text = $"{discoveredRoom.HostAddress}:{discoveredRoom.HostPort}";
            }

            TaskHelper.RunSafely(JoinAddressAsync(
                $"{discoveredRoom.HostAddress}:{discoveredRoom.HostPort}",
                discoveredRoom.HostAddress,
                discoveredRoom.HostPort,
                $"正在连接 {GetRoomDisplayText(discoveredRoom)}"));
        }

        /// <summary>
        /// 复用现有 ENet 加入逻辑，统一处理 discovery 自动加入和手动 IP 加入。
        /// </summary>
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

                DisplayServer.WindowSetTitle("杀戮尖塔 2（客户端）");
                await JoinFriendScreen.JoinGameAsync(
                    new ENetClientConnectionInitializer(SettingsService.Instance.SettingsModel.NetId, hostAddress, port));
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

        /// <summary>
        /// 用原页面里的按钮样式创建一个新按钮，保持视觉风格与原版一致。
        /// </summary>
        private NJoinFriendRefreshButton? CreateActionButton(string name, string text, Action onPressed)
        {
            if (JoinFriendScreen?.GetNode<NJoinFriendRefreshButton>("RefreshButton").Duplicate() is not
                NJoinFriendRefreshButton actionButton)
            {
                return null;
            }

            actionButton.Name = name;
            actionButton.CustomMinimumSize = new Vector2(300, 50);
            actionButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            actionButton.Connect(NClickableControl.SignalName.Released,
                Callable.From<NClickableControl>(_ => onPressed()));

            actionButton.Material = actionButton.Material.Duplicate() as Material;
            Traverse.Create(actionButton).Field("_hsv").SetValue(actionButton.Material);

            actionButton.GetNode<MegaLabel>("Label").SetTextAutoSize(text);
            return actionButton;
        }

        /// <summary>
        /// 复用原页面标题样式创建一个分组标题，避免面板视觉语言突兀。
        /// </summary>
        private MegaLabel? CreateSectionLabel(string text)
        {
            if (JoinFriendScreen?.GetNode("TitleLabel").Duplicate() is not MegaLabel sectionLabel)
                return null;

            sectionLabel.SetTextAutoSize(text);
            sectionLabel.CustomMinimumSize = new Vector2(300, 0);
            sectionLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            return sectionLabel;
        }

        /// <summary>
        /// 清空多房间模式下生成的房间按钮，为下一次刷新做准备。
        /// </summary>
        private void ClearDiscoveredRoomButtons()
        {
            if (_discoveredRoomContainer == null)
                return;

            foreach (var child in _discoveredRoomContainer.GetChildren())
            {
                child.QueueFree();
            }
        }

        /// <summary>
        /// 更新顶部状态提示，让玩家明确当前处于扫描、自动加入还是手动连接分支。
        /// </summary>
        private void SetStatus(string text)
        {
            _statusLabel?.SetTextAutoSize(text);
        }

        /// <summary>
        /// 组装房间按钮上的展示文案，尽量在一行里给出主机、模式和地址信息。
        /// </summary>
        private static string GetRoomDisplayText(LanDiscoveredRoomModel discoveredRoom)
        {
            var hostName = string.IsNullOrWhiteSpace(discoveredRoom.HostName) ? "未知主机" : discoveredRoom.HostName;
            var gameMode = string.IsNullOrWhiteSpace(discoveredRoom.GameMode) ? "未知模式" : discoveredRoom.GameMode;
            return $"{hostName} | {gameMode} | {discoveredRoom.HostAddress}:{discoveredRoom.HostPort}";
        }
    }
}
