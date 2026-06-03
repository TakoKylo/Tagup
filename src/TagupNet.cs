using System;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Tagup {
    /// <summary>
    /// Pushes the live HUD status (a phase string + a time string) from the server to clients via
    /// the Netcode CustomMessagingManager. Mirrors the community NetworkCommunication pattern
    /// (WriteValueSafe(string) + WriteBytesSafe(bytes)). The host reads state locally instead, so
    /// this only matters for remote clients.
    /// </summary>
    internal static class TagupNet {
        private const string MSG = "tagup_hud";

        // Latest values received by a (remote) client; read by the HUD overlay.
        internal static string ClientPhase = "CONTESTED";
        internal static string ClientTime = "FIRST TO 3";

        private static bool _clientRegistered;
        private static string _lastSent;
        private static float _nextHeartbeat;

        // ---------------------------------------------------------------- Server
        /// <summary>Broadcast current status to clients, but only when it changed or every ~2 s.</summary>
        internal static void ServerMaybeBroadcast(TagupConfig cfg) {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            (string phase, string time) = TagupStatus.Build(cfg);
            string combined = phase + "" + time;
            float now = Time.unscaledTime;
            if (combined == _lastSent && now < _nextHeartbeat) return;

            _lastSent = combined;
            _nextHeartbeat = now + 2f;
            Broadcast(phase, time);
        }

        private static void Broadcast(string phase, string time) {
            try {
                CustomMessagingManager cmm = NetworkManager.Singleton.CustomMessagingManager;
                if (cmm == null) return;

                byte[] data = Encoding.UTF8.GetBytes(time ?? "");
                int size = FastBufferWriter.GetWriteSize(phase ?? "") + sizeof(ulong) + FastBufferWriter.GetWriteSize(data);

                FastBufferWriter writer = new FastBufferWriter(size, Allocator.TempJob);
                try {
                    writer.WriteValueSafe(phase ?? "");
                    writer.WriteBytesSafe(data);
                    cmm.SendNamedMessageToAll(MSG, writer, NetworkDelivery.ReliableSequenced);
                }
                finally {
                    writer.Dispose();
                }
            }
            catch (Exception ex) {
                Log.Error("HUD broadcast failed: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- Client
        internal static void ClientRegister() {
            if (_clientRegistered) return;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.CustomMessagingManager == null) return;
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MSG, OnMessage);
            _clientRegistered = true;
        }

        internal static void ClientUnregister() {
            try {
                NetworkManager nm = NetworkManager.Singleton;
                if (nm != null && nm.CustomMessagingManager != null)
                    nm.CustomMessagingManager.UnregisterNamedMessageHandler(MSG);
            }
            catch { /* shutting down */ }
            _clientRegistered = false;
        }

        private static void OnMessage(ulong sender, FastBufferReader reader) {
            try {
                reader.ReadValueSafe(out string phase);
                int len = reader.Length - reader.Position;
                byte[] data = new byte[len];
                for (int i = 0; i < len; i++) reader.ReadByteSafe(out data[i]);

                ClientPhase = phase;
                ClientTime = Encoding.UTF8.GetString(data);
            }
            catch (Exception ex) {
                Log.Error("HUD message read failed: " + ex.Message);
            }
        }
    }
}
