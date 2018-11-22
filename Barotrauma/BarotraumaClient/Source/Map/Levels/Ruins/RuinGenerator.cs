﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.RuinGeneration
{
    partial class Ruin
    {
        public void DebugDraw(SpriteBatch spriteBatch)
        {
            foreach (RuinShape shape in allShapes)
            {
                GUI.DrawString(spriteBatch, new Vector2(shape.Center.X, -shape.Center.Y - 50), shape.DistanceFromEntrance.ToString(), Color.White, Color.Black * 0.5f, font: GUI.LargeFont);
            }
            foreach (Line line in walls)
            {
                GUI.DrawLine(spriteBatch, new Vector2(line.A.X, -line.A.Y), new Vector2(line.B.X, -line.B.Y), Color.Red, 0.0f, 10);
            }

#if DEBUG
            int iter = (int)(Timing.TotalTime / 3.0f) % hullSplitIter.Count;
            System.Diagnostics.Debug.WriteLine(iter);
            for (int i = 0; i<hullSplitIter[iter].Count;i++)
            {
                Rectangle drawRect = hullSplitIter[iter][i];
                drawRect.Y = -drawRect.Y;
                drawRect.Y -= drawRect.Height;
                GUI.DrawRectangle(spriteBatch, drawRect, 
                    Color.Lerp(Color.LightGreen, Color.Red, (float)i / hullSplitIter[iter].Count), thickness: 50);
            }
#endif
        }
    }
}
