using System;
using System.Collections.Generic;
using System.Text;

namespace KellMachineVision
{
    public interface IDraw
    {
        Guid ID { get; }
        string Name { get; set; }
        string Description { get; set; }
        ItemParam Param { get; }
        ItemValue Value { get; set; }
    }
}
