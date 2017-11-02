﻿using System;

namespace EfCore.Shaman
{
    [AttributeUsage(AttributeTargets.Property)]
    public class UnicodeTextAttribute : Attribute
    {
        public UnicodeTextAttribute(bool isUnicode = true)
        {
            IsUnicode = isUnicode;
        }

        public bool IsUnicode { get; }
    }
}