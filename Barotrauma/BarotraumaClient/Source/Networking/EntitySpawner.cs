﻿using Barotrauma.Networking;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        public void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer message, float sendingTime)
        {
            if (GameMain.Server != null) return;

            bool remove = message.ReadBoolean();

            if (remove)
            {
                ushort entityId = message.ReadUInt16();

                var entity = FindEntityByID(entityId);
                if (entity != null)
                {
                    entity.Remove();
                }
            }
            else
            {
                switch (message.ReadByte())
                {
                    case (byte)SpawnableType.Item:
                        Item.ReadSpawnData(message, true);
                        break;
                    case (byte)SpawnableType.Character:
                        Character.ReadSpawnData(message, true);
                        break;
                    case (byte)SpawnableType.Structure:
                        Structure.ReadSpawnData(message, true);
                        break;
                    default:
                        DebugConsole.ThrowError("Received invalid entity spawn message (unknown spawnable type)");
                        break;
                }
            }
        }
    }
}
