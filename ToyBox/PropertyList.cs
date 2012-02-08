﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;

namespace ToyBox
{
    public class SupplyDefaultValueEventArgs : EventArgs
    {
        public string Key { get; private set; }
        // TODO-john-2012: Create multiple properties/methods with the allowed types
        public object Value { get; set; }

        public SupplyDefaultValueEventArgs(string key)
        {
            this.Key = key;
            this.Value = null;
        }
    }

    public class PropertyList
    {
        public Dictionary<string, object> Dictionary { get; set; }
        
        public event EventHandler<SupplyDefaultValueEventArgs> SupplyDefaultValue;

        public PropertyList()
        {
            this.Dictionary = new Dictionary<string, object>();
        }

        public PropertyList(Dictionary<string, object> dict)
        {
            // TODO-john-2012: This should do a deep copy of the passed in dictionary
            this.Dictionary = dict;
        }

        public PropertyList DeepClone()
        {
            PropertyList propList = new PropertyList();

            propList.Dictionary = CloneDictionary(this.Dictionary);
            propList.SupplyDefaultValue = this.SupplyDefaultValue;

            return propList;
        }

        public bool Modified { get; set; }

        private Dictionary<string, object> CloneDictionary(Dictionary<string, object> fromDict)
        {
            Dictionary<string, object> newDict = new Dictionary<string,object>();

            foreach (var pair in fromDict)
            {
                if (pair.Value is Dictionary<string, object>)
                {
                    newDict.Add(pair.Key, CloneDictionary((Dictionary<string, object>)pair.Value));
                }
                else if (pair.Value is List<object>)
                {
                    throw new NotImplementedException();
                }
                else
                {
                    newDict.Add(pair.Key, pair.Value);
                }
            }

            return newDict;
        }

        private object GetValue(string name)
        {
            object obj;

            if (Dictionary.TryGetValue(name, out obj))
                return obj;

            obj = RaiseSupplyDefaultValueEvent(name);
            Dictionary[name] = obj;
            return obj;
        }

        private void SetValue(string name, object value)
        {
            Dictionary[name] = value;
            Modified = true;
        }

        public bool GetBoolean(string name)
        {
            try
            {
                return (bool)GetValue(name);
            }
            catch (InvalidCastException)
            {
                return (bool)RaiseSupplyDefaultValueEvent(name);
            }
        }

        public void Set(string name, bool b)
        {
            SetValue(name, (object)b);
        }

        public int GetInt32(string name)
        {
            try
            {
                return (int)GetValue(name);
            }
            catch (InvalidCastException)
            {
                return (int)RaiseSupplyDefaultValueEvent(name);
            }
        }

        public void Set(string name, int i)
        {
            SetValue(name, (object)i);
        }

        public DateTime GetDateTime(string name)
        {
            try
            {
                return (DateTime)GetValue(name);
            }
            catch (InvalidCastException)
            {
                return (DateTime)RaiseSupplyDefaultValueEvent(name);
            }
        }

        public void Set(string name, DateTime d)
        {
            SetValue(name, (object)d);
        }

        public TimeSpan GetTimeSpan(string name)
        {
            try
            {
                return (TimeSpan)GetValue(name);
            }
            catch (InvalidCastException)
            {
                return (TimeSpan)RaiseSupplyDefaultValueEvent(name);
            }
        }

        public void Set(string name, TimeSpan t)
        {
            SetValue(name, (object)t);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            using (XmlWriter xw = XmlWriter.Create(sb))
            {
                PropertyListWriter.WriteXml(xw, this);
            }

            return sb.ToString();
        }

        public static PropertyList FromXml(string xml)
        {
            PropertyList propertyList = null;

            try
            {
                using (StringReader sr = new StringReader(xml))
                {
                    using (XmlReader xr = XmlReader.Create(sr))
                    {
                        propertyList = PropertyListReaderV1.ReadXml(xr);
                    }
                }
            }
            catch (Exception)
            {
            }

            return propertyList;
        }

        private object RaiseSupplyDefaultValueEvent(string name)
        {
            SupplyDefaultValueEventArgs args = new SupplyDefaultValueEventArgs(name);

            if (SupplyDefaultValue != null)
            {
                foreach (var method in SupplyDefaultValue.GetInvocationList())
                {
                    method.Method.Invoke(SupplyDefaultValue.Target, new object[] {this, args});

                    if (args.Value != null)
                        break;
                }
            }

            return args.Value;
        }
    }
}
