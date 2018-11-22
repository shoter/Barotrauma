﻿using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable
    {
        const float NetworkUpdateInterval = 0.5f;

        public static List<Hull> hullList = new List<Hull>();
        public static List<EntityGrid> EntityGrids { get; } = new List<EntityGrid>();

        public static bool ShowHulls = true;

        public static bool EditWater, EditFire;
        public const float OxygenDistributionSpeed = 500.0f;
        public const float OxygenDetoriationSpeed = 0.3f;
        public const float OxygenConsumptionSpeed = 1000.0f;

        public const int WaveWidth = 32;
        public static float WaveStiffness = 0.02f;
        public static float WaveSpread = 0.05f;
        public static float WaveDampening = 0.05f;
        
        //how much excess water the room can contain  (= more than the volume of the room)
        public const float MaxCompress = 10000f;
        
        public readonly Dictionary<string, SerializableProperty> properties;
        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get { return properties; }
        }

        private float lethalPressure;

        private float surface, drawSurface;
        private float waterVolume;
        private float pressure;

        private float oxygen;

        private bool update;

        public bool Visible = true;
        
        float[] waveY; //displacement from the surface of the water
        float[] waveVel; //velocity of the point

        float[] leftDelta;
        float[] rightDelta;

        private float lastSentVolume, lastSentOxygen;
        private float sendUpdateTimer;
        
        public List<Gap> ConnectedGaps;

        public override string Name
        {
            get
            {
                return "Hull";
            }
        }
        
        [Editable, Serialize("", true)]
        public string RoomName
        {
            get;
            set;
        }

        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                base.Rect = value;

                if (Submarine == null || !Submarine.Loading)
                {
                    Item.UpdateHulls();
                    Gap.UpdateHulls();
                }

                surface = drawSurface = rect.Y - rect.Height + WaterVolume / rect.Width;
                Pressure = surface;
            }
        }
        
        public override bool Linkable
        {
            get { return true; }
        }

        public float LethalPressure
        {
            get { return lethalPressure; }
            set { lethalPressure = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public Vector2 Size
        {
            get { return new Vector2(rect.Width, rect.Height); }
        }

        public float CeilingHeight
        {
            get;
            private set;
        }

        public float Surface
        {
            get { return surface; }
        }

        public float DrawSurface
        {
            get { return drawSurface; }
            set
            {
                if (Math.Abs(drawSurface - value) < 0.00001f) return;
                drawSurface = MathHelper.Clamp(value, rect.Y - rect.Height, rect.Y);
                update = true;
            }
        }

        public float WorldSurface
        {
            get { return Submarine == null ? surface : surface + Submarine.Position.Y; }
        }

        public float WaterVolume
        {
            get { return waterVolume; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                waterVolume = MathHelper.Clamp(value, 0.0f, Volume + MaxCompress);
                if (waterVolume < Volume) Pressure = rect.Y - rect.Height + waterVolume / rect.Width;
                if (waterVolume > 0.0f) update = true;
            }
        }

        [Serialize(90.0f, true)]
        public float Oxygen
        {
            get { return oxygen; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                oxygen = MathHelper.Clamp(value, 0.0f, Volume); 
            }
        }

        public float OxygenPercentage
        {
            get { return oxygen / Volume * 100.0f; }
            set { Oxygen = (value / 100.0f) * Volume; }
        }

        public float Volume
        {
            get { return rect.Width * rect.Height; }
        }

        public float Pressure
        {
            get { return pressure; }
            set { pressure = value; }
        }

        public float[] WaveY
        {
            get { return waveY; }
        }

        public float[] WaveVel
        {
            get { return waveVel; }
        }

        public List<FireSource> FireSources { get; private set; }

        public Hull(MapEntityPrefab prefab, Rectangle rectangle)
            : this (prefab, rectangle, Submarine.MainSub)
        {

        }

        public Hull(MapEntityPrefab prefab, Rectangle rectangle, Submarine submarine)
            : base (prefab, submarine)
        {
            rect = rectangle;
            
            OxygenPercentage = 100.0f;

            FireSources = new List<FireSource>();

            properties = SerializableProperty.GetProperties(this);

            int arraySize = (int)Math.Ceiling((float)rectangle.Width / WaveWidth + 1);
            waveY = new float[arraySize];
            waveVel = new float[arraySize];

            leftDelta = new float[arraySize];
            rightDelta = new float[arraySize];

            surface = rect.Y - rect.Height;

            aiTarget = new AITarget(this);

            hullList.Add(this);

            ConnectedGaps = new List<Gap>();

            if (submarine == null || !submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            WaterVolume = 0.0f;

            InsertToList();
        }

        public static Rectangle GetBorders()
        {
            if (!hullList.Any()) return Rectangle.Empty;

            Rectangle rect = hullList[0].rect;
            
            foreach (Hull hull in hullList)
            {
                if (hull.Rect.X < rect.X)
                {
                    rect.Width += rect.X - hull.rect.X;
                    rect.X = hull.rect.X;

                }
                if (hull.rect.Right > rect.Right) rect.Width = hull.rect.Right - rect.X;

                if (hull.rect.Y > rect.Y)
                {
                    rect.Height += hull.rect.Y - rect.Y;

                    rect.Y = hull.rect.Y;
                }
                if (hull.rect.Y - hull.rect.Height < rect.Y - rect.Height) rect.Height = rect.Y - (hull.rect.Y - hull.rect.Height);
            }

            return rect;
        }

        public override MapEntity Clone()
        {
            return new Hull(MapEntityPrefab.Find(null, "hull"), rect, Submarine);
        }

        public static EntityGrid GenerateEntityGrid(Rectangle worldRect)
        {
            var newGrid = new EntityGrid(worldRect, 200.0f);
            EntityGrids.Add(newGrid);
            return newGrid;
        }

        public static EntityGrid GenerateEntityGrid(Submarine submarine)
        {
            var newGrid = new EntityGrid(submarine, 200.0f);
            EntityGrids.Add(newGrid);            
            foreach (Hull hull in hullList)
            {
                if (hull.Submarine == submarine) newGrid.InsertEntity(hull);
            }
            return newGrid;
        }

        public override void OnMapLoaded()
        {
            CeilingHeight = Rect.Height;

            Body lowerPickedBody = Submarine.PickBody(SimPosition, SimPosition - new Vector2(0.0f, ConvertUnits.ToSimUnits(rect.Height / 2.0f + 0.1f)), null, Physics.CollisionWall);
            if (lowerPickedBody != null)
            {
                Vector2 lowerPickedPos = Submarine.LastPickedPosition;

                if (Submarine.PickBody(SimPosition, SimPosition + new Vector2(0.0f, ConvertUnits.ToSimUnits(rect.Height / 2.0f + 0.1f)), null, Physics.CollisionWall) != null)
                {
                    Vector2 upperPickedPos = Submarine.LastPickedPosition;

                    CeilingHeight = ConvertUnits.ToDisplayUnits(upperPickedPos.Y - lowerPickedPos.Y);
                }
            }
        }

        public void AddToGrid(Submarine submarine)
        {
            foreach (EntityGrid grid in EntityGrids)
            {
                if (grid.Submarine != submarine) continue;

                rect.Location -= MathUtils.ToPoint(submarine.HiddenSubPosition);
                
                grid.InsertEntity(this);

                rect.Location += MathUtils.ToPoint(submarine.HiddenSubPosition);
                return;
            }
        }

        public int GetWaveIndex(Vector2 position)
        {
            return GetWaveIndex(position.X);
        }

        public int GetWaveIndex(float xPos)
        {
            int index = (int)(xPos - rect.X) / WaveWidth;
            index = (int)MathHelper.Clamp(index, 0, waveY.Length - 1);
            return index;
        }

        public override void Move(Vector2 amount)
        {
            rect.X += (int)amount.X;
            rect.Y += (int)amount.Y;

            if (Submarine == null || !Submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            surface = drawSurface = rect.Y - rect.Height + WaterVolume / rect.Width;
            Pressure = surface;
        }

        public override void ShallowRemove()
        {
            base.Remove();
            hullList.Remove(this);

            if (Submarine == null || (!Submarine.Loading && !Submarine.Unloading))
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            List<FireSource> fireSourcesToRemove = new List<FireSource>(FireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }
            FireSources.Clear();
            
            if (EntityGrids != null)
            {
                foreach (EntityGrid entityGrid in EntityGrids)
                {
                    entityGrid.RemoveEntity(this);
                }
            }
        }

        public override void Remove()
        {
            base.Remove();
            hullList.Remove(this);

            if (Submarine != null && !Submarine.Loading && !Submarine.Unloading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            List<FireSource> fireSourcesToRemove = new List<FireSource>(FireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }
            FireSources.Clear();
            
            if (EntityGrids != null)
            {
                foreach (EntityGrid entityGrid in EntityGrids)
                {
                    entityGrid.RemoveEntity(this);
                }
            }
        }

        public void AddFireSource(FireSource fireSource)
        {
            FireSources.Add(fireSource);

            if (GameMain.Server != null && !IdFreed) GameMain.Server.CreateEntityEvent(this);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime, cam);

            Oxygen -= OxygenDetoriationSpeed * deltaTime;

            FireSource.UpdateAll(FireSources, deltaTime);

            aiTarget.SightRange = Submarine == null ? 0.0f : Math.Max(Submarine.Velocity.Length() * 500.0f, 500.0f);
            aiTarget.SoundRange -= deltaTime * 1000.0f;
         
            //update client hulls if the amount of water has changed by >10%
            //or if oxygen percentage has changed by 5%
            if (Math.Abs(lastSentVolume - waterVolume) > Volume * 0.1f ||
                Math.Abs(lastSentOxygen - OxygenPercentage) > 5f)
            {
                if (GameMain.Server != null && !IdFreed)
                {
                    sendUpdateTimer -= deltaTime;
                    if (sendUpdateTimer < 0.0f)
                    {
                        GameMain.Server.CreateEntityEvent(this);
                        lastSentVolume = waterVolume;
                        lastSentOxygen = OxygenPercentage;
                        sendUpdateTimer = NetworkUpdateInterval;
                    }
                }
            }

            if (!update)
            {
                lethalPressure = 0.0f;
                return;
            }
            
            surface = Math.Max(MathHelper.Lerp(
                surface, 
                rect.Y - rect.Height + WaterVolume / rect.Width, 
                deltaTime * 10.0f), rect.Y - rect.Height);
            //interpolate the position of the rendered surface towards the "target surface"
            drawSurface = Math.Max(MathHelper.Lerp(
                drawSurface, 
                rect.Y - rect.Height + WaterVolume / rect.Width, 
                deltaTime * 10.0f), rect.Y - rect.Height);

            for (int i = 0; i < waveY.Length; i++)
            {
                //apply velocity
                waveY[i] = waveY[i] + waveVel[i];

                //if the wave attempts to go "through" the top of the hull, make it bounce back
                if (surface + waveY[i] > rect.Y)
                {
                    float excess = (surface + waveY[i]) - rect.Y;
                    waveY[i] -= excess;
                    waveVel[i] = waveVel[i] * -0.5f;
                }
                //if the wave attempts to go "through" the bottom of the hull, make it bounce back
                else if (surface + waveY[i] < rect.Y - rect.Height)
                {
                    float excess = (surface + waveY[i]) - (rect.Y - rect.Height);
                    waveY[i] -= excess;
                    waveVel[i] = waveVel[i] * -0.5f;
                }

                //acceleration
                float a = -WaveStiffness * waveY[i] - waveVel[i] * WaveDampening;
                waveVel[i] = waveVel[i] + a;
            }

            //apply spread (two iterations)
            for (int j = 0; j < 2; j++)
            {
                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    leftDelta[i] = WaveSpread * (waveY[i] - waveY[i - 1]);
                    waveVel[i - 1] += leftDelta[i];

                    rightDelta[i] = WaveSpread * (waveY[i] - waveY[i + 1]);
                    waveVel[i + 1] += rightDelta[i];
                }

                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    waveY[i - 1] += leftDelta[i];
                    waveY[i + 1] += rightDelta[i];
                }
            }

            //make waves propagate through horizontal gaps
            foreach (Gap gap in ConnectedGaps)
            {
                if (!gap.IsRoomToRoom || !gap.IsHorizontal || gap.Open <= 0.0f) continue;
                if (surface > gap.Rect.Y || surface < gap.Rect.Y - gap.Rect.Height) continue;

                Hull hull2 = this == gap.linkedTo[0] as Hull ? (Hull)gap.linkedTo[1] : (Hull)gap.linkedTo[0];
                float otherSurfaceY = hull2.surface;
                if (otherSurfaceY > gap.Rect.Y || otherSurfaceY < gap.Rect.Y - gap.Rect.Height) continue;

                float surfaceDiff = (surface - otherSurfaceY) * gap.Open;
                if (this != gap.linkedTo[0] as Hull)
                {
                    //the first hull linked to the gap handles the wave propagation, 
                    //the second just updates the surfaces to the same level
                    if (surfaceDiff < 32.0f)
                    {
                        hull2.waveY[hull2.waveY.Length - 1] = surfaceDiff * 0.5f;
                        waveY[0] = -surfaceDiff * 0.5f;
                    }
                    continue;
                }

                for (int j = 0; j < 2; j++)
                {
                    int i = waveY.Length - 1;

                    leftDelta[i] = WaveSpread * (waveY[i] - waveY[i - 1]);
                    waveVel[i - 1] += leftDelta[i];

                    rightDelta[i] = WaveSpread * (waveY[i] - hull2.waveY[0] + surfaceDiff);
                    hull2.waveVel[0] += rightDelta[i];

                    i = 0;

                    hull2.leftDelta[i] = WaveSpread * (hull2.waveY[i] - waveY[waveY.Length - 1] - surfaceDiff);
                    waveVel[waveVel.Length - 1] += hull2.leftDelta[i];

                    hull2.rightDelta[i] = WaveSpread * (hull2.waveY[i] - hull2.waveY[i + 1]);
                    hull2.waveVel[i + 1] += hull2.rightDelta[i];
                }

                if (surfaceDiff < 32.0f)
                {
                    //update surfaces to the same level
                    hull2.waveY[0] = surfaceDiff * 0.5f;
                    waveY[waveY.Length - 1] = -surfaceDiff * 0.5f;
                }
                else
                {
                    hull2.waveY[0] += rightDelta[waveY.Length - 1];
                    waveY[waveY.Length - 1] += hull2.leftDelta[0];
                }
            }
            
            if (waterVolume < Volume)
            {
                LethalPressure -= 10.0f * deltaTime;
                if (WaterVolume <= 0.0f)
                {
                    //wait for the surface to be lerped back to bottom and the waves to settle until disabling update
                    if (drawSurface > rect.Y - rect.Height + 1) return;
                    for (int i = 1; i < waveY.Length - 1; i++)
                    {
                        if (waveY[i] > 0.1f) return;
                    }

                    update = false;
                }
            }
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam);

        public void ApplyFlowForces(float deltaTime, Item item)
        {
            foreach (var gap in ConnectedGaps.Where(gap => gap.Open > 0))
            {
                //var pos = gap.Position - body.Position;
                var distance = MathHelper.Max(Vector2.DistanceSquared(item.Position, gap.Position)/1000, 1f);
               
                //pos.Normalize();
                item.body.ApplyForce((gap.LerpedFlowForce/distance) * deltaTime);
            }
        }

        public void Extinguish(float deltaTime, float amount, Vector2 position)
        {
            for (int i = FireSources.Count - 1; i >= 0; i-- )
            {
                FireSources[i].Extinguish(deltaTime, amount, position);
            }
        }

        public void RemoveFire(FireSource fire)
        {
            FireSources.Remove(fire);

            if (GameMain.Server != null) GameMain.Server.CreateEntityEvent(this);
        }

        public IEnumerable<Hull> GetConnectedHulls(int? searchDepth)
        {
            return GetAdjacentHulls(new HashSet<Hull>(), 0, searchDepth);
        }

        private HashSet<Hull> GetAdjacentHulls(HashSet<Hull> connectedHulls, int steps, int? searchDepth)
        {
            connectedHulls.Add(this);

            if (searchDepth != null && steps >= searchDepth.Value) return connectedHulls;

            foreach (Gap g in ConnectedGaps)
            {
                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    if (g.linkedTo[i] is Hull hull && !connectedHulls.Contains(hull))
                    {
                        hull.GetAdjacentHulls(connectedHulls, steps++, searchDepth);
                    }
                }
            }

            return connectedHulls;
        }

        /// <summary>
        /// Approximate distance from this hull to the target hull, moving through open gaps without passing through walls.
        /// Uses a greedy algo and may not use the most optimal path. Returns float.MaxValue if no path is found.
        /// </summary>
        public float GetApproximateDistance(Hull target, float maxDistance)
        {
            return GetApproximateHullDistance(new HashSet<Hull>(), target, 0.0f, maxDistance);
        }

        private float GetApproximateHullDistance(HashSet<Hull> connectedHulls, Hull target, float distance, float maxDistance)
        {
            if (distance >= maxDistance) return float.MaxValue;
            if (this == target) return distance;

            connectedHulls.Add(this);

            foreach (Gap g in ConnectedGaps)
            {
                if (g.ConnectedDoor != null)
                {
                    //gap blocked if the door is not open or the predicted state is not open
                    if (!g.ConnectedDoor.IsOpen || (g.ConnectedDoor.PredictedState.HasValue && !g.ConnectedDoor.PredictedState.Value))
                    {
                        if (g.ConnectedDoor.OpenState < 0.1f) continue;
                    }
                }
                else if (g.Open <= 0.0f)
                {
                    continue;
                }

                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    if (g.linkedTo[i] is Hull hull && !connectedHulls.Contains(hull))
                    {
                        float dist = hull.GetApproximateHullDistance(connectedHulls, target, distance + Vector2.Distance(g.Position, this.Position), maxDistance);
                        if (dist < float.MaxValue) return dist;
                    }
                }
            }

            return float.MaxValue;
        }

        //returns the water block which contains the point (or null if it isn't inside any)
        public static Hull FindHull(Vector2 position, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            if (EntityGrids == null) return null;

            if (guess != null)
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position, inclusive)) return guess;
            }

            foreach (EntityGrid entityGrid in EntityGrids)
            {
                if (entityGrid.Submarine != null && !entityGrid.Submarine.Loading)
                {
                    System.Diagnostics.Debug.Assert(!entityGrid.Submarine.Removed);
                    Rectangle borders = entityGrid.Submarine.Borders;
                    if (useWorldCoordinates)
                    {
                        Vector2 worldPos = entityGrid.Submarine.WorldPosition;
                        borders.Location += new Point((int)worldPos.X, (int)worldPos.Y);
                    }
                    else
                    {
                        borders.Location += new Point((int)entityGrid.Submarine.HiddenSubPosition.X, (int)entityGrid.Submarine.HiddenSubPosition.Y);
                    }

                    const float padding = 128.0f;
                    if (position.X < borders.X - padding || position.X > borders.Right + padding || 
                        position.Y > borders.Y + padding || position.Y < borders.Y - borders.Height - padding)
                    {
                        continue;
                    }
                }

                Vector2 transformedPosition = position;
                if (useWorldCoordinates && entityGrid.Submarine != null) transformedPosition -= entityGrid.Submarine.Position;

                var entities = entityGrid.GetEntities(transformedPosition);
                if (entities == null) continue;
                foreach (Hull hull in entities)
                {
                    if (Submarine.RectContains(hull.rect, transformedPosition, inclusive)) return hull;
                }
            }

            return null;
        }

        //returns the water block which contains the point (or null if it isn't inside any)
        public static Hull FindHullOld(Vector2 position, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            return FindHullOld(position, hullList, guess, useWorldCoordinates, inclusive);
        }

        public static Hull FindHullOld(Vector2 position, List<Hull> hulls, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            if (guess != null && hulls.Contains(guess))
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position, inclusive)) return guess;
            }

            foreach (Hull hull in hulls)
            {
                if (Submarine.RectContains(useWorldCoordinates ? hull.WorldRect : hull.rect, position, inclusive)) return hull;
            }

            return null;
        }

        public static void DetectItemVisibility(Character c=null)
        {
            if (c==null)
            {
                foreach (Item it in Item.ItemList)
                {
                    it.Visible = true;
                }
            }
            else
            {
                Hull h = c.CurrentHull;
                hullList.ForEach(j => j.Visible = false);
                List<Hull> visibleHulls;
                if (h == null || c.Submarine == null)
                {
                    visibleHulls = hullList.FindAll(j => j.CanSeeOther(null, false));
                }
                else
                {
                    visibleHulls = hullList.FindAll(j => h.CanSeeOther(j, true));
                }
                visibleHulls.ForEach(j => j.Visible = true);
                foreach (Item it in Item.ItemList)
                {
                    if (it.CurrentHull == null || visibleHulls.Contains(it.CurrentHull)) it.Visible = true;
                    else it.Visible = false;
                }
            }
        }

        private bool CanSeeOther(Hull other, bool allowIndirect = true)
        {
            if (other == this) return true;

            if (other != null && other.Submarine == Submarine)
            {
                bool retVal = false;
                foreach (Gap g in ConnectedGaps)
                {
                    if (g.ConnectedWall != null && g.ConnectedWall.CastShadow) continue;
                    List<Hull> otherHulls = hullList.FindAll(h => h.ConnectedGaps.Contains(g) && h != this);
                    retVal = otherHulls.Any(h => h == other);
                    if (!retVal && allowIndirect) retVal = otherHulls.Any(h => h.CanSeeOther(other, false));
                    if (retVal) return true;
                }
            }
            else
            {
                foreach (Gap g in ConnectedGaps)
                {
                    if (g.ConnectedDoor != null && !hullList.Any(h => h.ConnectedGaps.Contains(g) && h != this)) return true;
                }
                List<MapEntity> structures = mapEntityList.FindAll(me => me is Structure && me.Rect.Intersects(Rect));
                return structures.Any(st => !(st as Structure).CastShadow);
            }
            return false;
        }
        
        public string CreateRoomName()
        {
            List<string> roomItems = new List<string>();
            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull != this) continue;
                if (item.GetComponent<Items.Components.Reactor>() != null) roomItems.Add("reactor");
                if (item.GetComponent<Items.Components.Engine>() != null) roomItems.Add("engine");
                if (item.GetComponent<Items.Components.Steering>() != null) roomItems.Add("steering");
                if (item.GetComponent<Items.Components.Sonar>() != null) roomItems.Add("sonar");
                if (item.HasTag("ballast")) roomItems.Add("ballast");
            }

            if (roomItems.Contains("reactor"))
                return TextManager.Get("ReactorRoom");
            else if (roomItems.Contains("engine"))
                return TextManager.Get("EngineRoom");
            else if (roomItems.Contains("steering") && roomItems.Contains("sonar"))
                return TextManager.Get("CommandRoom");
            else if (roomItems.Contains("ballast"))
                return TextManager.Get("Ballast");

            if (ConnectedGaps.Any(g => !g.IsRoomToRoom && g.ConnectedDoor != null))
            {
                return TextManager.Get("Airlock");
            }

            Rectangle subRect = Submarine.CalculateDimensions();

            Alignment roomPos;
            if (rect.Y - rect.Height / 2 > subRect.Y + subRect.Height * 0.66f)
                roomPos = Alignment.Top;
            else if (rect.Y - rect.Height / 2 > subRect.Y + subRect.Height * 0.33f)
                roomPos = Alignment.CenterY;
            else
                roomPos = Alignment.Bottom;
            
            if (rect.Center.X < subRect.X + subRect.Width * 0.33f)
                roomPos |= Alignment.Left;
            else if (rect.Center.X < subRect.X + subRect.Width * 0.66f)
                roomPos |= Alignment.CenterX;
            else
                roomPos |= Alignment.Right;

            return TextManager.Get("Sub" + roomPos.ToString());
        }

        public void ServerWrite(NetBuffer message, Client c, object[] extraData = null)
        {
            message.WriteRangedSingle(MathHelper.Clamp(waterVolume / Volume, 0.0f, 1.5f), 0.0f, 1.5f, 8);
            message.WriteRangedSingle(MathHelper.Clamp(OxygenPercentage, 0.0f, 100.0f), 0.0f, 100.0f, 8);

            message.Write(FireSources.Count > 0);
            if (FireSources.Count > 0)
            {
                message.WriteRangedInteger(0, 16, Math.Min(FireSources.Count, 16));
                for (int i = 0; i < Math.Min(FireSources.Count, 16); i++)
                {
                    var fireSource = FireSources[i];
                    Vector2 normalizedPos = new Vector2(
                        (fireSource.Position.X - rect.X) / rect.Width,
                        (fireSource.Position.Y - (rect.Y - rect.Height)) / rect.Height);

                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.X, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.Y, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(fireSource.Size.X / rect.Width, 0.0f, 1.0f), 0, 1.0f, 8);
                }
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer message, float sendingTime)
        {
            WaterVolume = message.ReadRangedSingle(0.0f, 1.5f, 8) * Volume;
            OxygenPercentage = message.ReadRangedSingle(0.0f, 100.0f, 8);

            bool hasFireSources = message.ReadBoolean();
            int fireSourceCount = 0;
            
            if (hasFireSources)
            {
                fireSourceCount = message.ReadRangedInteger(0, 16);
                for (int i = 0; i < fireSourceCount; i++)
                {
                    Vector2 pos = Vector2.Zero;
                    float size = 0.0f;
                    pos.X = MathHelper.Clamp(message.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f);
                    pos.Y = MathHelper.Clamp(message.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f);
                    size = message.ReadRangedSingle(0.0f, 1.0f, 8);

                    pos = new Vector2(
                        rect.X + rect.Width * pos.X, 
                        rect.Y - rect.Height + (rect.Height * pos.Y));
                    size = size * rect.Width;
                    
                    var newFire = i < FireSources.Count ? 
                        FireSources[i] : 
                        new FireSource(Submarine == null ? pos : pos + Submarine.Position, null, true);
                    newFire.Position = pos;
                    newFire.Size = new Vector2(size, newFire.Size.Y);

                    //ignore if the fire wasn't added to this room (invalid position)?
                    if (!FireSources.Contains(newFire))
                    {
                        newFire.Remove();
                        continue;
                    }                    
                }
            }

            while (FireSources.Count > fireSourceCount)
            {
                FireSources[FireSources.Count - 1].Remove();
            }            
        }

        public static Hull Load(XElement element, Submarine submarine)
        {
            Rectangle rect = Rectangle.Empty;
            if (element.Attribute("rect") != null)
            {
                rect = element.GetAttributeRect("rect", Rectangle.Empty);
            }
            else
            {
                //backwards compatibility
                rect = new Rectangle(
                    int.Parse(element.Attribute("x").Value),
                    int.Parse(element.Attribute("y").Value),
                    int.Parse(element.Attribute("width").Value),
                    int.Parse(element.Attribute("height").Value));
            }

            return new Hull(MapEntityPrefab.Find(null, "hull"), rect, submarine)
            {
                waterVolume = element.GetAttributeFloat("pressure", 0.0f),
                ID = (ushort)int.Parse(element.Attribute("ID").Value)
            };
        }

        public override XElement Save(XElement parentElement)
        {
            if (Submarine == null)
            {
                string errorMsg = "Error - tried to save a hull that's not a part of any submarine.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Hull.Save:WorldHull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return null;
            }

            XElement element = new XElement("Hull");
            element.Add
            (
                new XAttribute("ID", ID),
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height),
                new XAttribute("water", waterVolume)
            );

            parentElement.Add(element);

            return element;
        }

    }
}
