using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;

namespace KellMachineVision
{
    [Serializable]
    public class ItemValue
    {
        string name;

        public string Name
        {
            get { return name; }
            set { name = value; }
        }
        IValue value;

        public IValue Value
        {
            get { return this.value; }
            set { this.value = value; }
        }

        public ItemValue(string name, IValue value)
        {
            this.name = name;
            this.value = value;
        }
    }
}
