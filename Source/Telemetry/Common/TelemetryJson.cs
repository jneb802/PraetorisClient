using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PraetorisClient
{
    internal sealed class TelemetryJson
    {
        private readonly StringBuilder _builder = new();
        private readonly Stack<bool> _needsComma = new();

        private TelemetryJson()
        {
        }

        internal static TelemetryJson Object()
        {
            TelemetryJson json = new();
            json._builder.Append('{');
            json._needsComma.Push(false);
            return json;
        }

        internal void End()
        {
            _builder.Append('}');
            if (_needsComma.Count > 0)
                _needsComma.Pop();
        }

        internal void BeginObject(string name)
        {
            Name(name);
            _builder.Append('{');
            _needsComma.Push(false);
        }

        internal void EndObject()
        {
            End();
        }

        internal void BeginArray(string name)
        {
            Name(name);
            _builder.Append('[');
            _needsComma.Push(false);
        }

        internal void EndArray()
        {
            _builder.Append(']');
            if (_needsComma.Count > 0)
                _needsComma.Pop();
        }

        internal void BeginArrayObject()
        {
            ArrayPrefix();
            _builder.Append('{');
            _needsComma.Push(false);
        }

        internal void EndArrayObject()
        {
            End();
        }

        internal void ArrayString(string value)
        {
            ArrayPrefix();
            WriteString(value);
        }

        internal void Prop(string name, string value)
        {
            Name(name);
            WriteString(value);
        }

        internal void Prop(string name, bool value)
        {
            Name(name);
            _builder.Append(value ? "true" : "false");
        }

        internal void Prop(string name, int value)
        {
            Name(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, uint value)
        {
            Name(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, short value)
        {
            Name(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, ushort value)
        {
            Name(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, long value)
        {
            Name(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, float value)
        {
            Name(name);
            if (float.IsNaN(value) || float.IsInfinity(value))
                _builder.Append("null");
            else
                _builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, double value)
        {
            Name(name);
            if (double.IsNaN(value) || double.IsInfinity(value))
                _builder.Append("null");
            else
                _builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, Vector3 value)
        {
            BeginObject(name);
            Prop("x", value.x);
            Prop("y", value.y);
            Prop("z", value.z);
            EndObject();
        }

        internal void Prop(string name, Quaternion value)
        {
            BeginObject(name);
            Prop("x", value.x);
            Prop("y", value.y);
            Prop("z", value.z);
            Prop("w", value.w);
            EndObject();
        }

        public override string ToString()
        {
            return _builder.ToString();
        }

        private void Name(string name)
        {
            if (_needsComma.Count == 0)
                throw new InvalidOperationException("JSON object is not open.");

            if (_needsComma.Peek())
                _builder.Append(',');

            _needsComma.Pop();
            _needsComma.Push(true);
            WriteString(name);
            _builder.Append(':');
        }

        private void ArrayPrefix()
        {
            if (_needsComma.Count == 0)
                throw new InvalidOperationException("JSON array is not open.");

            if (_needsComma.Peek())
                _builder.Append(',');

            _needsComma.Pop();
            _needsComma.Push(true);
        }

        private void WriteString(string value)
        {
            _builder.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"':
                        _builder.Append("\\\"");
                        break;
                    case '\\':
                        _builder.Append("\\\\");
                        break;
                    case '\b':
                        _builder.Append("\\b");
                        break;
                    case '\f':
                        _builder.Append("\\f");
                        break;
                    case '\n':
                        _builder.Append("\\n");
                        break;
                    case '\r':
                        _builder.Append("\\r");
                        break;
                    case '\t':
                        _builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                            _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _builder.Append(c);
                        break;
                }
            }

            _builder.Append('"');
        }
    }
}
