﻿using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WaterDetector : ItemComponent
    {
        private string output, falseOutput;

        [InGameEditable, Serialize("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, Serialize("0", true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set { falseOutput = value; }
        }

        public WaterDetector(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            string signalOut = item.InWater ? output : falseOutput;

            item.SendSignal(0, signalOut, "signal_out", null);
        }
    }
}
