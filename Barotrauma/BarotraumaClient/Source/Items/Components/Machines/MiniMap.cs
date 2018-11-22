﻿using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MiniMap : Powered
    {
        private GUIFrame submarineContainer;

        private GUIFrame hullInfoFrame;

        private GUITextBlock hullNameText, hullBreachText, hullAirQualityText, hullWaterText;

        private List<Submarine> displayedSubs = new List<Submarine>();

        partial void InitProjSpecific(XElement element)
        {
            new GUICustomComponent(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center),
                DrawHUDBack, null);
            submarineContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.85f), GuiFrame.RectTransform, Anchor.Center), style: null);

            hullInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.13f), GUI.Canvas, minSize: new Point(250, 150)),
                style: "InnerFrame")
            {
                CanBeFocused = false
            };
            var hullInfoContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), hullInfoFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            hullNameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), hullInfoContainer.RectTransform), "");
            hullBreachText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "");
            hullAirQualityText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "");
            hullWaterText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), hullInfoContainer.RectTransform), "");

            hullInfoFrame.Children.ForEach(c => { c.CanBeFocused = false; c.Children.ForEach(c2 => c2.CanBeFocused = false); });
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            if (hasPower) hullInfoFrame.AddToGUIUpdateList();
        }

        public override void OnMapLoaded()
        {
            base.OnMapLoaded();
            CreateHUD();
        }

        private void CreateHUD()
        {
            submarineContainer.ClearChildren();

            if (item.Submarine == null) return;

            item.Submarine.CreateMiniMap(submarineContainer);
            displayedSubs.Clear();
            displayedSubs.Add(item.Submarine);
            displayedSubs.AddRange(item.Submarine.DockedTo);
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            //recreate HUD if the subs we should display have changed
            if ((item.Submarine == null && displayedSubs.Count > 0) ||              //item not inside a sub anymore, but display is still showing subs
                !displayedSubs.Contains(item.Submarine) ||                          //current sub not displayer
                item.Submarine.DockedTo.Any(s => !displayedSubs.Contains(s)) ||     //some of the docked subs not diplayed
                displayedSubs.Any(s => s != item.Submarine && !item.Submarine.DockedTo.Contains(s))) //displaying a sub that shouldn't be displayed
            {
                CreateHUD();
            }

            float distort = 1.0f - item.Condition / 100.0f;
            foreach (HullData hullData in hullDatas.Values)
            {
                hullData.DistortionTimer -= deltaTime;
                if (hullData.DistortionTimer <= 0.0f)
                {
                    hullData.Distort = Rand.Range(0.0f, 1.0f) < distort * distort;
                    if (hullData.Distort)
                    {
                        hullData.Oxygen = Rand.Range(0.0f, 100.0f);
                        hullData.Water = Rand.Range(0.0f, 1.0f);
                    }
                    hullData.DistortionTimer = Rand.Range(1.0f, 10.0f);
                }
            }
        }

        private void DrawHUDBack(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            hullInfoFrame.Visible = false;
            if (item.Submarine == null || !hasPower)
            {
                foreach (Hull hull in Hull.hullList)
                {
                    var hullFrame = submarineContainer.Children.First().FindChild(hull);
                    if (hullFrame == null) continue;

                    hullFrame.Color = Color.DarkCyan * 0.3f;
                    hullFrame.Children.First().Color = Color.DarkCyan * 0.3f;
                }
            }

            float scale = 1.0f;
            HashSet<Submarine> subs = new HashSet<Submarine>();
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine == null) continue;
                var hullFrame = submarineContainer.Children.First().FindChild(hull);
                if (hullFrame == null) continue;

                hullDatas.TryGetValue(hull, out HullData hullData);
                if (hullData == null)
                {
                    hullData = new HullData();
                    hullDatas.Add(hull, hullData);
                }

                if (hullData.Distort)
                {
                    hullFrame.Children.First().Color = Color.Lerp(Color.Black, Color.DarkGray * 0.5f, Rand.Range(0.0f, 1.0f));
                    hullFrame.Color = Color.DarkGray * 0.5f;
                    continue;
                }
                
                subs.Add(hull.Submarine);
                scale = Math.Min(
                    hullFrame.Parent.Rect.Width / (float)hull.Submarine.Borders.Width, 
                    hullFrame.Parent.Rect.Height / (float)hull.Submarine.Borders.Height);
                
                Color borderColor = Color.DarkCyan;
                
                float? gapOpenSum = 0.0f;
                if (ShowHullIntegrity)
                {
                    gapOpenSum = hull.ConnectedGaps.Where(g => !g.IsRoomToRoom).Sum(g => g.Open);
                    borderColor = Color.Lerp(Color.DarkCyan, Color.Red, Math.Min((float)gapOpenSum, 1.0f));
                }

                float? oxygenAmount = null;
                if (!RequireOxygenDetectors || hullData?.Oxygen != null)
                {
                    oxygenAmount = RequireOxygenDetectors ? hullData.Oxygen : hull.OxygenPercentage;
                    GUI.DrawRectangle(spriteBatch, hullFrame.Rect, Color.Lerp(Color.Red * 0.5f, Color.Green * 0.3f, (float)oxygenAmount / 100.0f), true);
                }

                float? waterAmount = null;
                if (!RequireWaterDetectors || hullData.Water != null)
                {
                    waterAmount = RequireWaterDetectors ? hullData.Water : Math.Min(hull.WaterVolume / hull.Volume, 1.0f);
                    if (hullFrame.Rect.Height * waterAmount > 3.0f)
                    {
                        Rectangle waterRect = new Rectangle(
                            hullFrame.Rect.X, (int)(hullFrame.Rect.Y + hullFrame.Rect.Height * (1.0f - waterAmount)),
                            hullFrame.Rect.Width, (int)(hullFrame.Rect.Height * waterAmount));

                        waterRect.Inflate(-3, -3);

                        GUI.DrawRectangle(spriteBatch, waterRect, new Color(85, 136, 147), true);
                        GUI.DrawLine(spriteBatch, new Vector2(waterRect.X, waterRect.Y), new Vector2(waterRect.Right, waterRect.Y), Color.LightBlue);
                    }
                }

                if (GUI.MouseOn == hullFrame || hullFrame.IsParentOf(GUI.MouseOn))
                {
                    hullInfoFrame.RectTransform.ScreenSpaceOffset = hullFrame.Rect.Center;

                    hullInfoFrame.Visible = true;
                    hullNameText.Text = hull.RoomName;

                    hullBreachText.Text = gapOpenSum > 0.1f ? TextManager.Get("MiniMapHullBreach") : "";
                    hullBreachText.TextColor = Color.Red;

                    hullAirQualityText.Text = oxygenAmount == null ? TextManager.Get("MiniMapAirQualityUnavailable") : TextManager.Get("MiniMapAirQuality") + ": " + (int)oxygenAmount + " %";
                    hullAirQualityText.TextColor = oxygenAmount == null ? Color.Red : Color.Lerp(Color.Red, Color.LightGreen, (float)oxygenAmount / 100.0f);

                    hullWaterText.Text = waterAmount == null ? TextManager.Get("MiniMapWaterLevelUnavailable") : TextManager.Get("MiniMapWaterLevel") + ": " + (int)(waterAmount * 100.0f) + " %";
                    hullWaterText.TextColor = waterAmount == null ? Color.Red : Color.Lerp(Color.LightGreen, Color.Red, (float)waterAmount);

                    borderColor = Color.Lerp(borderColor, Color.White, 0.5f);
                    hullFrame.Children.First().Color = Color.White;
                }
                else
                {
                    hullFrame.Children.First().Color = Color.DarkCyan * 0.8f;
                }
                hullFrame.Color = borderColor;
            }

            foreach (Submarine sub in subs)
            {
                float displayScale = ConvertUnits.ToDisplayUnits(scale);
                Vector2 offset = Vector2.Zero;
                Vector2 center = container.Rect.Center.ToVector2();

                if (sub != item.Submarine && item.Submarine != null)
                {
                    offset = ConvertUnits.ToSimUnits(sub.WorldPosition - item.Submarine.WorldPosition);
                }

                for (int i = 0; i < sub.HullVertices.Count; i++)
                {
                    Vector2 start = (sub.HullVertices[i] + offset) * displayScale;
                    start += Vector2.Normalize(start) * 0;
                    start.Y = -start.Y;
                    Vector2 end = (sub.HullVertices[(i + 1) % sub.HullVertices.Count] + offset) * displayScale;
                    end += Vector2.Normalize(end) * 0;
                    end.Y = -end.Y;
                    GUI.DrawLine(spriteBatch, center + start, center + end, Color.DarkCyan * Rand.Range(0.3f, 0.35f), width: 10);
                }
            }
        }
    }
}
