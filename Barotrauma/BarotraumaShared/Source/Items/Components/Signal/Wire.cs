﻿using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Wire : ItemComponent, IDrawableComponent, IServerSerializable
    {
        partial class WireSection
        {
            private Vector2 start;

            private float angle;
            private float length;

            public WireSection(Vector2 start, Vector2 end)
            {
                this.start = start;

                angle = MathUtils.VectorToAngle(end - start);
                length = Vector2.Distance(start, end);
            }
        }

        const float nodeDistance = 32.0f;
        const float heightFromFloor = 128.0f;

        const int MaxNodeCount = 255;
        const int MaxNodesPerNetworkEvent = 30;

        private List<Vector2> nodes;
        private List<WireSection> sections;

        private Connection[] connections;

        private bool canPlaceNode;
        private Vector2 newNodePos;
        
        public bool Hidden, Locked;

        public Connection[] Connections
        {
            get { return connections; }
        }

        [Serialize(5000.0f, false)]
        public float MaxLength
        {
            get;
            set;
        }
                
        public Wire(Item item, XElement element)
            : base(item, element)
        {
#if CLIENT
            if (wireSprite == null)
            {
                wireSprite = new Sprite("Content/Items/wireHorizontal.png", new Vector2(0.5f, 0.5f));
                wireSprite.Depth = 0.85f;
            }
#endif

            nodes = new List<Vector2>();
            sections = new List<WireSection>();

            connections = new Connection[2];
            
            IsActive = false;
        }
                
        public Connection OtherConnection(Connection connection)
        {
            if (connection == null) return null;
            if (connection == connections[0]) return connections[1];
            if (connection == connections[1]) return connections[0];

            return null;
        }

        public bool IsConnectedTo(Item item)
        {
            if (connections[0] != null && connections[0].Item == item) return true;
            return (connections[1] != null && connections[1].Item == item);
        }

        public void RemoveConnection(Item item)
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null || connections[i].Item != item) continue;

                foreach (Wire wire in connections[i].Wires)
                {
                    if (wire != this) continue;
                    SetConnectedDirty();

                    connections[i].SetWire(connections[i].FindWireIndex(wire), null);
                }

                connections[i] = null;
            }
        }

        public void RemoveConnection(Connection connection)
        {
            if (connection == connections[0]) connections[0] = null;            
            if (connection == connections[1]) connections[1] = null;

            SetConnectedDirty();
        }

        public bool Connect(Connection newConnection, bool addNode = true, bool sendNetworkEvent = false)
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == newConnection) return false;
            }

            if (!connections.Any(c => c == null)) return false;

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null && connections[i].Item == newConnection.Item)
                {
                    addNode = false;
                    break;
                }
            }

            if (item.body != null) item.Submarine = newConnection.Item.Submarine;

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null) continue;

                connections[i] = newConnection;

                if (!addNode) break;

                Submarine refSub = newConnection.Item.Submarine;
                if (refSub == null)
                {
                    Structure attachTarget = Structure.GetAttachTarget(newConnection.Item.WorldPosition);
                    if (attachTarget == null) continue;
                    refSub = attachTarget.Submarine;
                }

                Vector2 nodePos = refSub == null ? 
                    newConnection.Item.Position : 
                    newConnection.Item.Position - refSub.HiddenSubPosition;


                if (nodes.Count > 0 && nodes[0] == nodePos) break;
                if (nodes.Count > 1 && nodes[nodes.Count - 1] == nodePos) break;

                if (i == 0)
                {
                    nodes.Insert(0, nodePos);                    
                }
                else
                {
                    nodes.Add(nodePos);
                }
                
                break;
            }

            SetConnectedDirty();

            if (connections[0] != null && connections[1] != null)
            {
                foreach (ItemComponent ic in item.components)
                {
                    if (ic == this) continue;
                    ic.Drop(null);
                }
                if (item.Container != null) item.Container.RemoveContained(this.item);
                if (item.body != null) item.body.Enabled = false;

                IsActive = false;

                CleanNodes();
            }
            
            if (item.body != null) item.Submarine = newConnection.Item.Submarine;

            if (sendNetworkEvent)
            {
                if (GameMain.Server != null)
                {
                    CreateNetworkEvent();
                }
                //the wire is active if only one end has been connected
                IsActive = connections[0] == null ^ connections[1] == null;
            }

            Drawable = IsActive || nodes.Any();

            UpdateSections();
            return true;
        }

        public override void Equip(Character character)
        {
            ClearConnections(character);

            IsActive = true;
        }

        public override void Unequip(Character character)
        {
            ClearConnections(character);

            IsActive = false;
        }

        public override void Drop(Character dropper)
        {
            ClearConnections(dropper);
            
            IsActive = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (nodes.Count == 0) return;

            Submarine sub = null;
            if (connections[0] != null && connections[0].Item.Submarine != null) sub = connections[0].Item.Submarine;
            if (connections[1] != null && connections[1].Item.Submarine != null) sub = connections[1].Item.Submarine;

            if (Screen.Selected != GameMain.SubEditorScreen)
            {
                //cannot run wires from sub to another
                if (item.Submarine != sub && sub != null && item.Submarine != null)
                {
                    ClearConnections();
                    return;
                }

                if (item.CurrentHull == null)
                {
                    Structure attachTarget = Structure.GetAttachTarget(item.WorldPosition);
                    canPlaceNode = attachTarget != null;

                    sub = attachTarget?.Submarine;
                    newNodePos = sub == null ? 
                        item.WorldPosition :
                        item.WorldPosition - sub.Position - sub.HiddenSubPosition;
                }
                else
                {
                    Vector2 newPos = item.Position;

                    if (item.ParentInventory?.Owner == Character.Controlled)
                    {
                        newPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition) - sub.Position;
                        var distance = Vector2.Distance(newPos, Character.Controlled.Position);

                        if (distance > 110f)
                            newPos = item.Position;
                    }

                    newNodePos = RoundNode(newPos, item.CurrentHull) - sub.HiddenSubPosition;
                    canPlaceNode = true;
                }

                //prevent the wire from extending too far when rewiring
                if (nodes.Count > 0)
                {
                    Character user = item.ParentInventory?.Owner as Character;
                    if (user == null) return;

                    Vector2 prevNodePos = nodes[nodes.Count - 1];
                    if (sub != null) prevNodePos += sub.HiddenSubPosition;

                    float currLength = 0.0f;
                    for (int i = 0; i < nodes.Count - 1; i++)
                    {
                        currLength += Vector2.Distance(nodes[i], nodes[i + 1]);
                    }
                    currLength += Vector2.Distance(nodes[nodes.Count - 1], newNodePos);

                    if (currLength > MaxLength)
                    {
                        Vector2 diff = nodes[nodes.Count - 1] - newNodePos;
                        Vector2 pullBackDir = diff == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(diff);

                        user.AnimController.Collider.ApplyForce(pullBackDir * user.Mass * 50.0f);
                        user.AnimController.UpdateUseItem(true, user.SimPosition + pullBackDir * 2.0f);
                        if (currLength > MaxLength * 1.5f && GameMain.Client == null)
                        {
                            ClearConnections();
                            CreateNetworkEvent();
                            return;
                        }
                    }
                }
            }
            else
            {
                newNodePos = RoundNode(item.Position, item.CurrentHull) - sub.HiddenSubPosition;
                canPlaceNode = true;
            }
        }
        
        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null) return false;
            if (character == Character.Controlled && character.SelectedConstruction != null) return false;

            if (newNodePos != Vector2.Zero && canPlaceNode && nodes.Count > 0 && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
            {
                if (nodes.Count >= MaxNodeCount)
                {
                    nodes.RemoveAt(nodes.Count - 1);
                }

                nodes.Add(newNodePos);
                CleanNodes();
                UpdateSections();
                Drawable = true;
                newNodePos = Vector2.Zero;

                if (GameMain.Server != null)
                {
                    CreateNetworkEvent();
                }
            }
            return true;
        }

        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (nodes.Count > 1)
            {
                nodes.RemoveAt(nodes.Count - 1);
                UpdateSections();
            }

            Drawable = IsActive || sections.Count > 0;
            return true;
        }

        public override bool Pick(Character picker)
        {
            ClearConnections(picker);

            return true;
        }

        public override void Move(Vector2 amount)
        {
#if CLIENT
            if (item.IsSelected) MoveNodes(amount);
#endif
        }

        public List<Vector2> GetNodes()
        {
            return new List<Vector2>(nodes);
        }

        public void SetNodes(List<Vector2> nodes)
        {
            this.nodes = new List<Vector2>(nodes);
            UpdateSections();
        }

        public void MoveNode(int index, Vector2 amount)
        {
            if (index < 0 || index >= nodes.Count) return;
            nodes[index] += amount;            
            UpdateSections();
        }

        public void MoveNodes(Vector2 amount)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] += amount;
            }
            UpdateSections();
        }

        public void UpdateSections()
        {
            sections.Clear();

            for (int i = 0; i < nodes.Count-1; i++)
            {
                sections.Add(new WireSection(nodes[i], nodes[i + 1]));
            }
            Drawable = IsActive || sections.Count > 0;
        }

        private void ClearConnections(Character user = null)
        {
            nodes.Clear();
            sections.Clear();
            
            if (user != null)
            {
                if (connections[0] != null && connections[1] != null)
                {
                    GameServer.Log(user.LogName + " disconnected a wire from " + 
                        connections[0].Item.Name + " (" + connections[0].Name + ") to "+
                        connections[1].Item.Name + " (" + connections[1].Name + ")", ServerLog.MessageType.ItemInteraction);
                }
                else if (connections[0] != null)
                {
                    GameServer.Log(user.LogName + " disconnected a wire from " +
                        connections[0].Item.Name + " (" + connections[0].Name + ")", ServerLog.MessageType.ItemInteraction);
                }
                else if (connections[1] != null)
                {
                    GameServer.Log(user.LogName + " disconnected a wire from " +
                        connections[1].Item.Name + " (" + connections[1].Name + ")", ServerLog.MessageType.ItemInteraction);
                }
            }
            
            SetConnectedDirty();

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) continue;
                int wireIndex = connections[i].FindWireIndex(item);

                if (wireIndex == -1) continue;
                connections[i].SetWire(wireIndex, null);

                connections[i] = null;
            }

            Drawable = sections.Count > 0;
        }

        private Vector2 RoundNode(Vector2 position, Hull hull)
        {
            if (Screen.Selected == GameMain.SubEditorScreen)
            {
                position.X = MathUtils.Round(position.X, Submarine.GridSize.X / 2.0f);
                position.Y = MathUtils.Round(position.Y, Submarine.GridSize.Y / 2.0f);
            }
            else
            {
                position.X = MathUtils.Round(position.X, nodeDistance);
                if (hull == null)
                {
                    position.Y = MathUtils.Round(position.Y, nodeDistance);
                }
                else
                {
                    position.Y -= hull.Rect.Y - hull.Rect.Height;
                    position.Y = Math.Max(MathUtils.Round(position.Y, nodeDistance), heightFromFloor);
                    position.Y += hull.Rect.Y -hull.Rect.Height;
                }
            }

            return position;
        }

        public void SetConnectedDirty()
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i]?.Item != null)
                {
                    var pt = connections[i].Item.GetComponent<PowerTransfer>();
                    if (pt != null) pt.SetConnectionDirty(connections[i]);
                }
            }
        }

        private void CleanNodes()
        {
            bool removed;
            do
            {
                removed = false;
                for (int i = nodes.Count - 2; i > 0; i--)
                {
                    if (Math.Abs(nodes[i - 1].X - nodes[i].X) < 1.0f && Math.Abs(nodes[i + 1].X - nodes[i].X) < 1.0f &&
                        Math.Sign(nodes[i - 1].Y - nodes[i].Y) != Math.Sign(nodes[i + 1].Y - nodes[i].Y))
                    {
                        nodes.RemoveAt(i);
                        removed = true;
                    }
                    else if (Math.Abs(nodes[i - 1].Y - nodes[i].Y) < 1.0f && Math.Abs(nodes[i + 1].Y - nodes[i].Y) < 1.0f &&
                            Math.Sign(nodes[i - 1].X - nodes[i].X) != Math.Sign(nodes[i + 1].X - nodes[i].X))
                    {
                        nodes.RemoveAt(i);
                        removed = true;
                    }
                }

            } while (removed);

        }

        private int GetClosestNodeIndex(Vector2 pos, float maxDist, out float closestDist)
        {
            closestDist = 0.0f;
            int closestIndex = -1;

            for (int i = 0; i < nodes.Count; i++)
            {
                float dist = Vector2.Distance(nodes[i], pos);
                if (dist > maxDist) continue;

                if (closestIndex == -1 || dist < closestDist)
                {
                    closestIndex = i;
                    closestDist = dist;
                }
            }

            return closestIndex;
        }

        private int GetClosestSectionIndex(Vector2 mousePos, float maxDist, out float closestDist)
        {
            closestDist = 0.0f;
            int closestIndex = -1;

            for (int i = 0; i < nodes.Count-1; i++)
            {
                if ((Math.Abs(nodes[i].X - nodes[i + 1].X)<5 || Math.Sign(mousePos.X - nodes[i].X) != Math.Sign(mousePos.X - nodes[i + 1].X)) &&
                     (Math.Abs(nodes[i].Y - nodes[i + 1].Y)<5 || Math.Sign(mousePos.Y - nodes[i].Y) != Math.Sign(mousePos.Y - nodes[i + 1].Y)))
                {
                    float dist = MathUtils.LineToPointDistance(nodes[i], nodes[i + 1], mousePos);
                    if (dist > maxDist) continue;

                    if (closestIndex == -1 || dist < closestDist)
                    {
                        closestIndex = i;
                        closestDist = dist;
                    }
                }
            }

            return closestIndex;
        }
        
        public override void FlipX(bool relativeToSub)
        {            
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] = new Vector2(-nodes[i].X, nodes[i].Y);
            }
            UpdateSections();
        }

        public override void FlipY(bool relativeToSub)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] = new Vector2(nodes[i].X, -nodes[i].Y);
            }
            UpdateSections();
        }

        public override void Load(XElement componentElement)
        {
            base.Load(componentElement);

            string nodeString = componentElement.GetAttributeString("nodes", "");
            if (nodeString == "") return;

            string[] nodeCoords = nodeString.Split(';');
            for (int i = 0; i < nodeCoords.Length / 2; i++)
            {
                float x = 0.0f, y = 0.0f;

                float.TryParse(nodeCoords[i * 2], NumberStyles.Float, CultureInfo.InvariantCulture, out x);

                float.TryParse(nodeCoords[i * 2 + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);

                nodes.Add(new Vector2(x, y));
            }

            Drawable = nodes.Any();
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            if (nodes == null || nodes.Count == 0) return componentElement;

            string[] nodeCoords = new string[nodes.Count * 2];
            for (int i = 0; i < nodes.Count; i++)
            {
                nodeCoords[i * 2] = nodes[i].X.ToString(CultureInfo.InvariantCulture);
                nodeCoords[i * 2 + 1] = nodes[i].Y.ToString(CultureInfo.InvariantCulture);
            }

            componentElement.Add(new XAttribute("nodes", string.Join(";", nodeCoords)));

            return componentElement;
        }

        protected override void ShallowRemoveComponentSpecific()
        {
            /*for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) continue;
                int wireIndex = connections[i].FindWireIndex(item);

                if (wireIndex > -1)
                {
                    connections[i].AddLink(wireIndex, null);
                }
            }*/
        }

        protected override void RemoveComponentSpecific()
        {
            ClearConnections();

            base.RemoveComponentSpecific();
        }

        private void CreateNetworkEvent()
        {
            if (GameMain.Server == null) return;
            //split into multiple events because one might not be enough to fit all the nodes
            int eventCount = Math.Max((int)Math.Ceiling(nodes.Count / (float)MaxNodesPerNetworkEvent), 1);
            for (int i = 0; i < eventCount; i++)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.components.IndexOf(this), i });
            }

        }
                
        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            int eventIndex = (int)extraData[2];
            int nodeStartIndex = eventIndex * MaxNodesPerNetworkEvent;
            int nodeCount = MathHelper.Clamp(nodes.Count - nodeStartIndex, 0, MaxNodesPerNetworkEvent);

            msg.WriteRangedInteger(0, (int)Math.Ceiling(MaxNodeCount / (float)MaxNodesPerNetworkEvent), eventIndex);
            msg.WriteRangedInteger(0, MaxNodesPerNetworkEvent, nodeCount);
            for (int i = nodeStartIndex; i < nodeStartIndex + nodeCount; i++)
            {
                msg.Write(nodes[i].X);
                msg.Write(nodes[i].Y);
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            int eventIndex = msg.ReadRangedInteger(0, (int)Math.Ceiling(MaxNodeCount / (float)MaxNodesPerNetworkEvent));
            int nodeCount = msg.ReadRangedInteger(0, MaxNodesPerNetworkEvent);
            int nodeStartIndex = eventIndex * MaxNodesPerNetworkEvent;

            Vector2[] nodePositions = new Vector2[nodeStartIndex + nodeCount];
            for (int i = 0; i < nodes.Count && i < nodePositions.Length; i++)
            {
                nodePositions[i] = nodes[i];
            }

            for (int i = 0; i < nodeCount; i++)
            {
                nodePositions[nodeStartIndex + i] = new Vector2(msg.ReadFloat(), msg.ReadFloat());
            }

            if (nodePositions.Any(n => !MathUtils.IsValid(n)))
            {
                nodes.Clear();
                return;
            }

            nodes = nodePositions.ToList();
            UpdateSections();
            Drawable = nodes.Any();
        }
        
    }
}
