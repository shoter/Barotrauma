﻿using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class Wire : ItemComponent, IDrawableComponent, IServerSerializable
    {
        partial class WireSection
        {
            public void Draw(SpriteBatch spriteBatch, Color color, Vector2 offset, float depth, float width = 0.3f)
            {
                spriteBatch.Draw(wireSprite.Texture,
                    new Vector2(start.X + offset.X, -(start.Y + offset.Y)), null, color,
                    -angle,
                    new Vector2(0.0f, wireSprite.size.Y / 2.0f),
                    new Vector2(length / wireSprite.Texture.Width, width),
                    SpriteEffects.None,
                    depth);
            }

            public static void Draw(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float depth, float width = 0.3f)
            {
                start.Y = -start.Y;
                end.Y = -end.Y;

                spriteBatch.Draw(wireSprite.Texture,
                    start, null, color,
                    MathUtils.VectorToAngle(end - start),
                    new Vector2(0.0f, wireSprite.size.Y / 2.0f),
                    new Vector2((Vector2.Distance(start, end)) / wireSprite.Texture.Width, width),
                    SpriteEffects.None,
                    depth);
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (sections.Count == 0 && !IsActive)
            {
                Drawable = false;
                return;
            }

            Vector2 drawOffset = Vector2.Zero;
            if (item.Submarine != null)
            {
                drawOffset = item.Submarine.DrawPosition + item.Submarine.HiddenSubPosition;
            }

            float depth = item.IsSelected ? 0.0f : wireSprite.Depth + ((item.ID % 100) * 0.00001f);

            if (item.IsHighlighted)
            {
                foreach (WireSection section in sections)
                {
                    section.Draw(spriteBatch, Color.Gold, drawOffset, depth + 0.00001f, 0.7f);
                }
            }
            else if (item.IsSelected)
            {
                foreach (WireSection section in sections)
                {
                    section.Draw(spriteBatch, Color.Red, drawOffset, depth + 0.00001f, 0.7f);
                }
            }

            foreach (WireSection section in sections)
            {
                section.Draw(spriteBatch, item.Color, drawOffset, depth, 0.3f);
            }

            if (IsActive && nodes.Count > 0 && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
            {
                //draw wires only for player that is actually connecting those wires!
                if(Character.Controlled == Item.ParentInventory.Owner)
                   
                WireSection.Draw(
                    spriteBatch,
                    new Vector2(nodes[nodes.Count - 1].X, nodes[nodes.Count - 1].Y) + drawOffset,
                    new Vector2(newNodePos.X, newNodePos.Y) + drawOffset,
                    item.Color * 0.5f,
                    depth,
                    0.3f);
            }

            if (!editing || !GameMain.SubEditorScreen.WiringMode) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2 drawPos = nodes[i];
                if (item.Submarine != null) drawPos += item.Submarine.Position + item.Submarine.HiddenSubPosition;
                drawPos.Y = -drawPos.Y;

                if (item.IsSelected)
                {
                    GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-5, -5), new Vector2(10, 10), item.Color, true, 0.0f);

                    if (highlightedNodeIndex == i)
                    {
                        GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-10, -10), new Vector2(20, 20), Color.Red, false, 0.0f);
                    }
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-3, -3), new Vector2(6, 6), item.Color, true, 0.0f);
                }
            }
        }

        public static void UpdateEditing(List<Wire> wires)
        {
            //dragging a node of some wire
            if (draggingWire != null)
            {
                //cancel dragging
                if (!PlayerInput.LeftButtonHeld())
                {
                    draggingWire = null;
                    selectedNodeIndex = null;
                }
                //update dragging
                else
                {
                    MapEntity.DisableSelect = true;

                    Submarine sub = null;
                    if (draggingWire.connections[0] != null && draggingWire.connections[0].Item.Submarine != null) sub = draggingWire.connections[0].Item.Submarine;
                    if (draggingWire.connections[1] != null && draggingWire.connections[1].Item.Submarine != null) sub = draggingWire.connections[1].Item.Submarine;

                    Vector2 nodeWorldPos = GameMain.SubEditorScreen.Cam.ScreenToWorld(PlayerInput.MousePosition) - sub.HiddenSubPosition - sub.Position;// Nodes[(int)selectedNodeIndex];

                    nodeWorldPos.X = MathUtils.Round(nodeWorldPos.X, Submarine.GridSize.X / 2.0f);
                    nodeWorldPos.Y = MathUtils.Round(nodeWorldPos.Y, Submarine.GridSize.Y / 2.0f);

                    draggingWire.nodes[(int)selectedNodeIndex] = nodeWorldPos;
                    draggingWire.UpdateSections();

                    MapEntity.SelectEntity(draggingWire.item);
                }

                return;
            }

            //a wire has been selected -> check if we should start dragging one of the nodes
            float nodeSelectDist = 10, sectionSelectDist = 5;
            highlightedNodeIndex = null;
            if (MapEntity.SelectedList.Count == 1 && MapEntity.SelectedList[0] is Item)
            {
                Wire selectedWire = ((Item)MapEntity.SelectedList[0]).GetComponent<Wire>();

                if (selectedWire != null)
                {
                    Vector2 mousePos = GameMain.SubEditorScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    if (selectedWire.item.Submarine != null) mousePos -= (selectedWire.item.Submarine.Position + selectedWire.item.Submarine.HiddenSubPosition);

                    //left click while holding ctrl -> check if the cursor is on a wire section, 
                    //and add a new node if it is
                    if (PlayerInput.KeyDown(Keys.RightControl) || PlayerInput.KeyDown(Keys.LeftControl))
                    {
                        if (PlayerInput.LeftButtonClicked())
                        {
                            float temp = 0.0f;
                            int closestSectionIndex = selectedWire.GetClosestSectionIndex(mousePos, sectionSelectDist, out temp);

                            if (closestSectionIndex > -1)
                            {
                                selectedWire.nodes.Insert(closestSectionIndex + 1, mousePos);
                                selectedWire.UpdateSections();
                            }
                        }
                    }
                    else
                    {
                        //check if close enough to a node
                        float temp = 0.0f;
                        int closestIndex = selectedWire.GetClosestNodeIndex(mousePos, nodeSelectDist, out temp);
                        if (closestIndex > -1)
                        {
                            highlightedNodeIndex = closestIndex;
                            //start dragging the node
                            if (PlayerInput.LeftButtonHeld())
                            {
                                draggingWire = selectedWire;
                                selectedNodeIndex = closestIndex;
                            }
                            //remove the node
                            else if (PlayerInput.RightButtonClicked() && closestIndex > 0 && closestIndex < selectedWire.nodes.Count - 1)
                            {
                                selectedWire.nodes.RemoveAt(closestIndex);
                                selectedWire.UpdateSections();
                            }
                        }
                    }
                }
            }

            //check which wire is highlighted with the cursor
            Wire highlighted = null;
            float closestDist = 0.0f;
            foreach (Wire w in wires)
            {
                Vector2 mousePos = GameMain.SubEditorScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                if (w.item.Submarine != null) mousePos -= (w.item.Submarine.Position + w.item.Submarine.HiddenSubPosition);

                float dist = 0.0f;
                if (w.GetClosestNodeIndex(mousePos, highlighted == null ? nodeSelectDist : closestDist, out dist) > -1)
                {
                    highlighted = w;
                    closestDist = dist;
                }

                if (w.GetClosestSectionIndex(mousePos, highlighted == null ? sectionSelectDist : closestDist, out dist) > -1)
                {
                    highlighted = w;
                    closestDist = dist;
                }

            }


            if (highlighted != null)
            {
                highlighted.item.IsHighlighted = true;

                if (PlayerInput.LeftButtonClicked())
                {
                    MapEntity.DisableSelect = true;
                    MapEntity.SelectEntity(highlighted.item);
                }
            }
        }
    }
}
