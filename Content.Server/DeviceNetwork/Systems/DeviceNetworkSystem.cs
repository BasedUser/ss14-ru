using Content.Server.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using static Content.Server.DeviceNetwork.Components.DeviceNetworkComponent;

namespace Content.Server.DeviceNetwork.Systems
{
    /// <summary>
    ///     Entity system that handles everything device network related.
    ///     Device networking allows machines and devices to communicate with each other while adhering to restrictions like range or being connected to the same powernet.
    /// </summary>
    [UsedImplicitly]
    public sealed class DeviceNetworkSystem : EntitySystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IPrototypeManager _protoMan = default!;
        [Dependency] private readonly SharedTransformSystem _transformSystem = default!;

        private readonly Dictionary<int, DeviceNet> _networks = new(4);
        private readonly Queue<DeviceNetworkPacketEvent> _packets = new();

        public override void Initialize()
        {
            SubscribeLocalEvent<DeviceNetworkComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<DeviceNetworkComponent, ComponentShutdown>(OnNetworkShutdown);
        }

        public override void Update(float frameTime)
        {
            while (_packets.TryDequeue(out var packet))
            {
                SendPacket(packet);
            }
        }

        /// <summary>
        /// Sends the given payload as a device network packet to the entity with the given address and frequency.
        /// Addresses are given to the DeviceNetworkComponent of an entity when connecting.
        /// </summary>
        /// <param name="uid">The EntityUid of the sending entity</param>
        /// <param name="address">The address of the entity that the packet gets sent to. If null, the message is broadcast to all devices on that frequency (except the sender)</param>
        /// <param name="frequency">The frequency to send on</param>
        /// <param name="data">The data to be sent</param>
        public void QueuePacket(EntityUid uid, string? address, NetworkPayload data, uint? frequency = null, DeviceNetworkComponent? device = null)
        {
            if (!Resolve(uid, ref device, false))
                return;

            if (device.Address == string.Empty)
                return;

            frequency ??= device.TransmitFrequency;

            if (frequency != null)
                _packets.Enqueue(new DeviceNetworkPacketEvent(device.DeviceNetId, address, frequency.Value, device.Address, uid, data));
        }
        /// <summary>
        /// Automatically attempt to connect some devices when a map starts.
        /// </summary>
        private void OnMapInit(EntityUid uid, DeviceNetworkComponent device, MapInitEvent args)
        {
            if (device.ReceiveFrequency == null
                && device.ReceiveFrequencyId != null
                && _protoMan.TryIndex<DeviceFrequencyPrototype>(device.ReceiveFrequencyId, out var receive))
            {
                device.ReceiveFrequency = receive.Frequency;
            }

            if (device.TransmitFrequency == null
                && device.TransmitFrequencyId != null
                && _protoMan.TryIndex<DeviceFrequencyPrototype>(device.TransmitFrequencyId, out var xmit))
            {
                device.TransmitFrequency = xmit.Frequency;
            }

            if (device.AutoConnect)
                ConnectDevice(uid, device);
        }

        private DeviceNet GetNetwork(int netId)
        {
            if (_networks.TryGetValue(netId, out var deviceNet))
                return deviceNet;
            var newDeviceNet = new DeviceNet(netId, _random);
            _networks[netId] = newDeviceNet;
            return newDeviceNet;
        }

        /// <summary>
        /// Automatically disconnect when an entity with a DeviceNetworkComponent shuts down.
        /// </summary>
        private void OnNetworkShutdown(EntityUid uid, DeviceNetworkComponent component, ComponentShutdown args)
        {
            GetNetwork(component.DeviceNetId).Remove(component);
        }

        /// <summary>
        /// Connect an entity with a DeviceNetworkComponent. Note that this will re-use an existing address if the
        /// device already had one configured. If there is a clash, the device cannot join the network.
        /// </summary>
        public bool ConnectDevice(EntityUid uid, DeviceNetworkComponent? device = null)
        {
            if (!Resolve(uid, ref device, false))
                return false;

            return GetNetwork(device.DeviceNetId).Add(device);
        }

        /// <summary>
        /// Disconnect an entity with a DeviceNetworkComponent.
        /// </summary>
        public bool DisconnectDevice(EntityUid uid, DeviceNetworkComponent? device, bool preventAutoConnect = true)
        {
            if (!Resolve(uid, ref device, false))
                return false;

            // If manually disconnected, don't auto reconnect when a game state is loaded.
            if (preventAutoConnect)
                device.AutoConnect = false;

            return GetNetwork(device.DeviceNetId).Remove(device);
        }

        public void SetReceiveFrequency(EntityUid uid, uint? frequency, DeviceNetworkComponent? device = null)
        {
            if (!Resolve(uid, ref device, false))
                return;

            if (device.ReceiveFrequency == frequency) return;

            var deviceNet = GetNetwork(device.DeviceNetId);
            deviceNet.Remove(device);
            device.ReceiveFrequency = frequency;
            deviceNet.Add(device);
        }

        public void SetTransmitFrequency(EntityUid uid, uint? frequency, DeviceNetworkComponent? device = null)
        {
            if (Resolve(uid, ref device, false))
                device.TransmitFrequency = frequency;
        }

        public void SetReceiveAll(EntityUid uid, bool receiveAll, DeviceNetworkComponent? device = null)
        {
            if (!Resolve(uid, ref device, false))
                return;

            if (device.ReceiveAll == receiveAll) return;

            var deviceNet = GetNetwork(device.DeviceNetId);
            deviceNet.Remove(device);
            device.ReceiveAll = receiveAll;
            deviceNet.Add(device);
        }

        public void SetAddress(EntityUid uid, string address, DeviceNetworkComponent? device = null)
        {
            if (!Resolve(uid, ref device, false))
                return;

            if (device.Address == address && device.CustomAddress == true) return;

            var deviceNet = GetNetwork(device.DeviceNetId);
            deviceNet.Remove(device);
            device.CustomAddress = true;
            device.Address = address;
            deviceNet.Add(device);
        }

        public void RandomizeAddress(EntityUid uid, DeviceNetworkComponent? device = null)
        {
            if (!Resolve(uid, ref device, false))
                return;
            var deviceNet = GetNetwork(device.DeviceNetId);
            deviceNet.Remove(device);
            device.CustomAddress = false;
            device.Address = "";
            deviceNet.Add(device);
        }

        /// <summary>
        ///     Try to find a device on a network using its address.
        /// </summary>
        private bool TryGetDevice(int netId, string address, [NotNullWhen(true)] out DeviceNetworkComponent? device) =>
            GetNetwork(netId).Devices.TryGetValue(address, out device);

        private void SendPacket(DeviceNetworkPacketEvent packet)
        {
            var network = GetNetwork(packet.NetId);
            if (packet.Address == null)
            {
                // Broadcast to all listening devices
                if (network.ListeningDevices.TryGetValue(packet.Frequency, out var devices) && CheckRecipientsList(packet, ref devices))
                {
                    var deviceCopy = ArrayPool<DeviceNetworkComponent>.Shared.Rent(devices.Count);
                    devices.CopyTo(deviceCopy);
                    SendToConnections(deviceCopy.AsSpan(0, devices.Count), packet);
                    ArrayPool<DeviceNetworkComponent>.Shared.Return(deviceCopy);
                }
            }
            else
            {
                var totalDevices = 0;
                var hasTargetedDevice = false;
                if (network.ReceiveAllDevices.TryGetValue(packet.Frequency, out var devices))
                {
                    totalDevices += devices.Count;
                }
                if (TryGetDevice(packet.NetId, packet.Address, out var device) &&
                    !device.ReceiveAll &&
                    device.ReceiveFrequency == packet.Frequency)
                {
                    totalDevices += 1;
                    hasTargetedDevice = true;
                }
                var deviceCopy = ArrayPool<DeviceNetworkComponent>.Shared.Rent(totalDevices);
                if (devices != null)
                {
                    devices.CopyTo(deviceCopy);
                }
                if (hasTargetedDevice)
                {
                    deviceCopy[totalDevices - 1] = device!;
                }
                SendToConnections(deviceCopy.AsSpan(0, totalDevices), packet);
                ArrayPool<DeviceNetworkComponent>.Shared.Return(deviceCopy);
            }
        }

        /// <summary>
        /// Sends the <see cref="BeforeBroadcastAttemptEvent"/> to the sending entity if the packets SendBeforeBroadcastAttemptEvent field is set to true.
        /// The recipients is set to the modified recipient list.
        /// </summary>
        /// <returns>false if the broadcast was canceled</returns>
        private bool CheckRecipientsList(DeviceNetworkPacketEvent packet, ref HashSet<DeviceNetworkComponent> recipients)
        {
            if (!_networks.ContainsKey(packet.NetId) || !_networks[packet.NetId].Devices.ContainsKey(packet.SenderAddress))
                return false;

            var sender = _networks[packet.NetId].Devices[packet.SenderAddress];
            if (!sender.SendBroadcastAttemptEvent)
                return true;

            var beforeBroadcastAttemptEvent = new BeforeBroadcastAttemptEvent(recipients);
            RaiseLocalEvent(packet.Sender, beforeBroadcastAttemptEvent);

            if (beforeBroadcastAttemptEvent.Cancelled || beforeBroadcastAttemptEvent.ModifiedRecipients == null)
                return false;

            recipients = beforeBroadcastAttemptEvent.ModifiedRecipients;
            return true;
        }

        private void SendToConnections(ReadOnlySpan<DeviceNetworkComponent> connections, DeviceNetworkPacketEvent packet)
        {
            var xform = Transform(packet.Sender);

            BeforePacketSentEvent beforeEv = new(packet.Sender, xform, _transformSystem.GetWorldPosition(xform));

            foreach (var connection in connections)
            {
                if (connection.Owner == packet.Sender)
                    continue;

                RaiseLocalEvent(connection.Owner, beforeEv, false);

                if (!beforeEv.Cancelled)
                    RaiseLocalEvent(connection.Owner, packet, false);
                else
                    beforeEv.Uncancel();
            }
        }
    }

    /// <summary>
    /// Event raised before a device network packet is send.
    /// Subscribed to by other systems to prevent the packet from being sent.
    /// </summary>
    public sealed class BeforePacketSentEvent : CancellableEntityEventArgs
    {
        /// <summary>
        /// The EntityUid of the entity the packet was sent from.
        /// </summary>
        public readonly EntityUid Sender;

        public readonly TransformComponent SenderTransform;

        /// <summary>
        ///     The senders current position in world coordinates.
        /// </summary>
        public readonly Vector2 SenderPosition;

        public BeforePacketSentEvent(EntityUid sender, TransformComponent xform, Vector2 senderPosition)
        {
            Sender = sender;
            SenderTransform = xform;
            SenderPosition = senderPosition;
        }
    }

    /// <summary>
    /// Sent to the sending entity before broadcasting network packets to recipients
    /// </summary>
    public sealed class BeforeBroadcastAttemptEvent : CancellableEntityEventArgs
    {
        public readonly IReadOnlySet<DeviceNetworkComponent> Recipients;
        public HashSet<DeviceNetworkComponent>? ModifiedRecipients;

        public BeforeBroadcastAttemptEvent(IReadOnlySet<DeviceNetworkComponent> recipients)
        {
            Recipients = recipients;
        }
    }

    /// <summary>
    /// Event raised when a device network packet gets sent.
    /// </summary>
    public sealed class DeviceNetworkPacketEvent : EntityEventArgs
    {
        /// <summary>
        /// The id of the network that this packet is being sent on.
        /// </summary>
        public int NetId;

        /// <summary>
        /// The frequency the packet is sent on.
        /// </summary>
        public readonly uint Frequency;

        /// <summary>
        /// Address of the intended recipient. Null if the message was broadcast.
        /// </summary>
        public string? Address;

        /// <summary>
        /// The device network address of the sending entity.
        /// </summary>
        public readonly string SenderAddress;

        /// <summary>
        /// The entity that sent the packet.
        /// </summary>
        public EntityUid Sender;

        /// <summary>
        /// The data that is being sent.
        /// </summary>
        public readonly NetworkPayload Data;

        public DeviceNetworkPacketEvent(int netId, string? address, uint frequency, string senderAddress, EntityUid sender, NetworkPayload data)
        {
            NetId = netId;
            Address = address;
            Frequency = frequency;
            SenderAddress = senderAddress;
            Sender = sender;
            Data = data;
        }
    }
}
