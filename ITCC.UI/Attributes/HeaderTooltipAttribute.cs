﻿using System;

namespace ITCC.UI.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class HeaderTooltipAttribute : Attribute
    {
        public HeaderTooltipAttribute(string tooltipContent, bool isLongTooltip = false)
        {
            TooltipContent = tooltipContent;
            IsLongTooltip = isLongTooltip;
        }

        public readonly string TooltipContent;

        public readonly bool IsLongTooltip;
    }
}
