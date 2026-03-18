using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using SlayTheSpire2.LAN.Multiplayer.Models;

namespace SlayTheSpire2.LAN.Multiplayer.Services
{
    internal class LanDiscoveryService
    {
        private sealed class LanDiscoveryHostContext
        {
            public required string GameMode { get; init; }
            public required ushort HostPort { get; init; }
            public required int MaxPlayers { get; init; }
        }

        private static readonly Lazy<LanDiscoveryService> Lazy = new(() => new LanDiscoveryService());

        public static LanDiscoveryService Instance => Lazy.Value;

        private readonly object _hostSyncRoot = new();

        private CancellationTokenSource? _hostCancellationTokenSource;
        private UdpClient? _hostListener;

        /// <summary>
        /// 启动局域网房间发现响应服务，让其他客户端可以通过广播发现当前主机。
        /// </summary>
        public void StartHostDiscovery(ushort hostPort, int maxPlayers, string gameMode)
        {
            StopHostDiscovery();

            try
            {
                var hostListener = new UdpClient(AddressFamily.InterNetwork);
                hostListener.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscoveryProtocol.Port));

                var cancellationTokenSource = new CancellationTokenSource();
                var hostContext = new LanDiscoveryHostContext
                {
                    HostPort = hostPort,
                    MaxPlayers = maxPlayers,
                    GameMode = gameMode
                };

                lock (_hostSyncRoot)
                {
                    _hostListener = hostListener;
                    _hostCancellationTokenSource = cancellationTokenSource;
                }

                _ = ListenForDiscoveryAsync(hostListener, hostContext, cancellationTokenSource.Token);
            }
            catch (Exception exception)
            {
                Log.Warn($"Failed to start LAN discovery host: {exception.Message}");
                StopHostDiscovery();
            }
        }

        /// <summary>
        /// 停止局域网房间发现响应服务，避免在退出房间后继续响应广播。
        /// </summary>
        public void StopHostDiscovery()
        {
            lock (_hostSyncRoot)
            {
                _hostCancellationTokenSource?.Cancel();
                _hostCancellationTokenSource?.Dispose();
                _hostCancellationTokenSource = null;

                _hostListener?.Dispose();
                _hostListener = null;
            }
        }

        /// <summary>
        /// 扫描当前局域网中的可加入房间，并返回去重后的房间列表。
        /// </summary>
        public async Task<IReadOnlyList<LanDiscoveredRoomModel>> DiscoverRoomsAsync(int timeoutMs,
            CancellationToken cancellationToken = default)
        {
            using var discoveryClient = new UdpClient(AddressFamily.InterNetwork);

            discoveryClient.EnableBroadcast = true;
            discoveryClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(new LanDiscoveryRequestModel());

            // 同时向全局广播地址和各网卡广播地址发送，提升不同系统/网卡组合下的发现成功率。
            foreach (var endPoint in GetDiscoveryBroadcastEndpoints())
            {
                await discoveryClient.SendAsync(requestBytes, requestBytes.Length, endPoint);
            }

            using var timeoutCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellationTokenSource.CancelAfter(timeoutMs);

            var discoveredRooms = new Dictionary<string, LanDiscoveredRoomModel>(StringComparer.Ordinal);

            try
            {
                while (!timeoutCancellationTokenSource.IsCancellationRequested)
                {
                    var receiveResult = await discoveryClient.ReceiveAsync(timeoutCancellationTokenSource.Token);
                    if (!TryDeserializeModel(receiveResult.Buffer, out LanDiscoveryResponseModel? discoveryResponse) ||
                        discoveryResponse == null ||
                        discoveryResponse.Magic != LanDiscoveryProtocol.Magic ||
                        discoveryResponse.Version != LanDiscoveryProtocol.Version)
                    {
                        continue;
                    }

                    var room = new LanDiscoveredRoomModel
                    {
                        HostAddress = receiveResult.RemoteEndPoint.Address.ToString(),
                        HostName = discoveryResponse.HostName,
                        HostPort = discoveryResponse.HostPort,
                        GameMode = discoveryResponse.GameMode,
                        MaxPlayers = discoveryResponse.MaxPlayers
                    };

                    discoveredRooms[$"{room.HostAddress}:{room.HostPort}"] = room;
                }
            }
            catch (OperationCanceledException)
            {
                // 超时或主动取消都属于预期流程，直接返回已发现的房间列表即可。
            }

            return discoveredRooms.Values
                .OrderBy(room => room.HostName, StringComparer.Ordinal)
                .ThenBy(room => room.HostAddress, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// 在后台持续监听 discovery 请求，并向请求方回送当前房间信息。
        /// </summary>
        private async Task ListenForDiscoveryAsync(UdpClient hostListener, LanDiscoveryHostContext hostContext,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var receiveResult = await hostListener.ReceiveAsync(cancellationToken);
                    if (!TryDeserializeModel(receiveResult.Buffer, out LanDiscoveryRequestModel? discoveryRequest) ||
                        discoveryRequest == null ||
                        discoveryRequest.Magic != LanDiscoveryProtocol.Magic ||
                        discoveryRequest.Version != LanDiscoveryProtocol.Version)
                    {
                        continue;
                    }

                    var responseBytes = JsonSerializer.SerializeToUtf8Bytes(new LanDiscoveryResponseModel
                    {
                        HostName = GetHostDisplayName(),
                        HostPort = hostContext.HostPort,
                        GameMode = hostContext.GameMode,
                        MaxPlayers = hostContext.MaxPlayers
                    });

                    await hostListener.SendAsync(responseBytes, responseBytes.Length, receiveResult.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                // 关闭 discovery 时会走到这里，属于预期行为。
            }
            catch (ObjectDisposedException)
            {
                // 关闭 UDP 监听器时会触发释放异常，属于预期行为。
            }
            catch (Exception exception)
            {
                Log.Warn($"LAN discovery host loop stopped unexpectedly: {exception.Message}");
            }
        }

        /// <summary>
        /// 解析广播/响应报文；报文非法时返回 false，避免 discovery 因异常中断。
        /// </summary>
        private static bool TryDeserializeModel<TModel>(byte[] payload, out TModel? result)
        {
            try
            {
                result = JsonSerializer.Deserialize<TModel>(Encoding.UTF8.GetString(payload));
                return result != null;
            }
            catch
            {
                result = default;
                return false;
            }
        }

        /// <summary>
        /// 生成本机需要尝试发送的广播地址列表。
        /// </summary>
        private static IEnumerable<IPEndPoint> GetDiscoveryBroadcastEndpoints()
        {
            var seenAddresses = new HashSet<string>(StringComparer.Ordinal)
            {
                IPAddress.Broadcast.ToString()
            };

            yield return new IPEndPoint(IPAddress.Broadcast, LanDiscoveryProtocol.Port);

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    var broadcastAddress = TryGetBroadcastAddress(unicastAddress);
                    if (broadcastAddress == null || !seenAddresses.Add(broadcastAddress.ToString()))
                    {
                        continue;
                    }

                    yield return new IPEndPoint(broadcastAddress, LanDiscoveryProtocol.Port);
                }
            }
        }

        /// <summary>
        /// 根据网卡的 IPv4 地址和子网掩码计算对应广播地址。
        /// </summary>
        private static IPAddress? TryGetBroadcastAddress(UnicastIPAddressInformation unicastAddress)
        {
            if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork || unicastAddress.IPv4Mask == null)
            {
                return null;
            }

            var ipAddressBytes = unicastAddress.Address.GetAddressBytes();
            var maskBytes = unicastAddress.IPv4Mask.GetAddressBytes();
            var broadcastBytes = new byte[ipAddressBytes.Length];

            for (var index = 0; index < ipAddressBytes.Length; index++)
            {
                broadcastBytes[index] = (byte)(ipAddressBytes[index] | ~maskBytes[index]);
            }

            return new IPAddress(broadcastBytes);
        }

        /// <summary>
        /// 生成房间展示用的主机名称，优先使用玩家设置名，兜底为机器名。
        /// </summary>
        private static string GetHostDisplayName()
        {
            var playerName = SettingsService.Instance.SettingsModel.PlayerName;
            return string.IsNullOrWhiteSpace(playerName) ? Environment.MachineName : playerName.Trim();
        }
    }
}
