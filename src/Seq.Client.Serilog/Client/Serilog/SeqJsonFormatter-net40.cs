﻿// Seq Client for .NET - Copyright 2014 Continuous IT Pty Ltd
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Parsing;

namespace Seq.Client.Serilog
{
    class SeqJsonFormatter : ITextFormatter
    {
        readonly bool _trailingNewline;
        readonly IDictionary<Type, Action<object, bool, TextWriter>> _literalWriters;

        public SeqJsonFormatter(bool trailingNewline = false)
        {
            _trailingNewline = trailingNewline;
            _literalWriters = new Dictionary<Type, Action<object, bool, TextWriter>>
            {
                { typeof(bool), (v, q, w) => WriteBoolean((bool)v, w) },
                { typeof(char), (v, q, w) => WriteString(((char)v).ToString(CultureInfo.InvariantCulture), w) },
                { typeof(byte), WriteToString },
                { typeof(sbyte), WriteToString },
                { typeof(short), WriteToString },
                { typeof(ushort), WriteToString },
                { typeof(int), WriteToString },
                { typeof(uint), WriteToString },
                { typeof(long), WriteToString },
                { typeof(ulong), WriteToString },
                { typeof(float), WriteToString },
                { typeof(double), WriteToString },
                { typeof(decimal), WriteToString },
                { typeof(string), (v, q, w) => WriteString((string)v, w) },
                { typeof(DateTime), (v, q, w) => WriteDateTime((DateTime)v, w) },
                { typeof(DateTimeOffset), (v, q, w) => WriteOffset((DateTimeOffset)v, w) },
                { typeof(ScalarValue), (v, q, w) => WriteLiteral(((ScalarValue)v).Value, w) },
                { typeof(SequenceValue), (v, q, w) => WriteSequence(((SequenceValue)v).Elements, w) },
                { typeof(DictionaryValue), (v, q, w) => WriteDictionary(((DictionaryValue)v).Elements, w) },
                { typeof(StructureValue), (v, q, w) => WriteStructure(((StructureValue)v).TypeTag, ((StructureValue)v).Properties, w) },
            };
        }

        public void Format(LogEvent logEvent, TextWriter output)
        {
            if (logEvent == null) throw new ArgumentNullException("logEvent");
            if (output == null) throw new ArgumentNullException("output");

            output.Write("{");

            var delim = "";
            WriteJsonProperty("Timestamp", logEvent.Timestamp, ref delim, output);
            WriteJsonProperty("Level", logEvent.Level, ref delim, output);
            WriteJsonProperty("MessageTemplate", logEvent.MessageTemplate.Text, ref delim, output);
    
            if (logEvent.Exception != null)
                WriteJsonProperty("Exception", logEvent.Exception, ref delim, output);

            if (logEvent.Properties.Count != 0)
            {
                output.Write(",\"Properties\":{");
                var pdelim = "";
                foreach (var property in logEvent.Properties)
                {
                    WriteJsonProperty(property.Key, property.Value, ref pdelim, output);
                }
                output.Write("}");
            }

            var tokensWithFormat = logEvent.MessageTemplate.Tokens
                .OfType<PropertyToken>()
                .Where(pt => pt.Format != null)
                .GroupBy(pt => pt.PropertyName)
                .ToArray();

            if (tokensWithFormat.Length != 0)
            {
                output.Write(",\"Renderings\":{");
                var rdelim = "";
                foreach (var ptoken in tokensWithFormat)
                {
                    output.Write(rdelim);
                    rdelim = ",";
                    WritePropertyName(ptoken.Key, output);
                    output.Write("[");

                    var fdelim = "";
                    foreach (var format in ptoken)
                    {
                        output.Write(fdelim);
                        fdelim = ",";

                        output.Write("{");
                        var eldelim = "";

                        WriteJsonProperty("Format", format.Format, ref eldelim, output);

                        var sw = new StringWriter();
                        format.Render(logEvent.Properties, sw);
                        WriteJsonProperty("Rendering", sw.ToString(), ref eldelim, output);

                        output.Write("}");
                    }

                    output.Write("]");
                }
                output.Write("}");
            }

            output.Write("}");

            if (_trailingNewline)
                output.WriteLine();
        }

        void WriteStructure(string typeTag, IEnumerable<LogEventProperty> properties, TextWriter output)
        {
            output.Write("{");

            var delim = "";
            if (typeTag != null)
                WriteJsonProperty("$typeTag", typeTag, ref delim, output);

            foreach (var property in properties)
                WriteJsonProperty(property.Name, property.Value, ref delim, output);

            output.Write("}");
        }

        void WriteSequence(IEnumerable elements, TextWriter output)
        {
            output.Write("[");
            foreach (var value in elements)
            {
                WriteLiteral(value, output);
                output.Write(",");
            }
            output.Write("]");
        }

        void WriteDictionary(IDictionary<ScalarValue, LogEventPropertyValue> elements, TextWriter output)
        {
            output.Write("{");
            var delim = "";
            foreach (var e in elements)
            {
                output.Write(delim);
                delim = ",";
                WriteLiteral(e.Key, output, true);
                output.Write(":");
                WriteLiteral(e.Value, output);
            }
            output.Write("}");
        }

        void WriteJsonProperty(string name, object value, ref string precedingDelimiter, TextWriter output)
        {
            output.Write(precedingDelimiter);
            WritePropertyName(name, output);
            WriteLiteral(value, output);
            precedingDelimiter = ",";
        }

        static void WritePropertyName(string name, TextWriter output)
        {
            output.Write("\"");
            output.Write(name);
            output.Write("\":");
        }

        void WriteLiteral(object value, TextWriter output, bool forceQuotation = false)
        {
            if (value == null)
            {
                output.Write("null");
                return;
            }

            Action<object, bool, TextWriter> writer;
            if (_literalWriters.TryGetValue(value.GetType(), out writer))
            {
                writer(value, forceQuotation, output);
                return;
            }

            WriteString(value.ToString(), output);
        }

        static void WriteToString(object number, bool quote, TextWriter output)
        {
            if (quote) output.Write('"');

            var fmt = number as IFormattable;
            if (fmt != null)
                output.Write(fmt.ToString(null, CultureInfo.InvariantCulture));
            else    
                output.Write(number.ToString());

            if (quote) output.Write('"');
        }

        static void WriteBoolean(bool value, TextWriter output)
        {
            output.Write(value ? "true" : "false");
        }

        static void WriteOffset(DateTimeOffset value, TextWriter output)
        {
            output.Write("\"");
            output.Write(value.ToString("o"));
            output.Write("\"");
        }

        static void WriteDateTime(DateTime value, TextWriter output)
        {
            output.Write("\"");
            output.Write(value.ToString("o"));
            output.Write("\"");
        }

        static void WriteString(string value, TextWriter output)
        {
            var content = Escape(value);
            output.Write("\"");
            output.Write(content);
            output.Write("\"");
        }

        static string Escape(string s)
        {
            return global::Serilog.Formatting.Json.JsonFormatter.Escape(s);
        }
    }
}
