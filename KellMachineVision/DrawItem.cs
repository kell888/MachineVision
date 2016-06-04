using System;
using System.Collections.Generic;
using System.Text;

namespace KellMachineVision
{
    [Serializable]
    public class DrawItem : IDraw
    {
        Guid id;
        string name;
        string description;
        ItemValue value;
        ItemParam param;

        public ItemParam Param
        {
            get { return param; }
        }

        public ItemValue Value
        {
            get { return value; }
            set { this.value = value; }
        }

        public DrawItem(string name, ItemParam param, ItemValue value = null)
        {
            this.id = Guid.NewGuid();
            this.name = name;
            this.param = param;
            if (value != null)
                this.value = value;
        }

        public Guid ID
        {
            get { return id; }
        }

        public string Name
        {
            get
            {
                return name;
            }
            set
            {
                name = value;
            }
        }

        public string Description
        {
            get
            {
                return description;
            }
            set
            {
                description = value;
            }
        }
    }
}
