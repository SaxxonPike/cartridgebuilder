using CartridgeBuilder.Lib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CartridgeBuilder
{
    struct Parameter
    {
        public string Key;
        public string Value;

        public Parameter(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }

    class Parser
    {
        /*
         * Precedence:
         * 
         * //       comments
         * { }      brackets (sections)
         * ;        semicolon (end of statement)
         * =        equals (key)
         * ,        commas (parameters)
         * :        colon (subkey)
         */

        protected int index = 0;
        protected string[] lines;

        static string hexAlphabet = "0123456789ABCDEF";
        static string decAlphabet = "0123456789";

        public void AssertClose()
        {
            bool result = (lines[index] == "}");
            Debug.Assert(result, "Expected }");
            index++;
        }

        public void AssertColon()
        {
            bool result = (lines[index] == ":");
            Debug.Assert(result, "Expected :");
            index++;
        }

        public void AssertComma()
        {
            bool result = (lines[index] == ",");
            Debug.Assert(result, "Expected ,");
            index++;
        }

        public void AssertEnd()
        {
            bool result = (lines[index] == ";");
            Debug.Assert(result, "Expected ;");
            index++;
        }

        public void AssertEquals()
        {
            bool result = (lines[index] == "=");
            Debug.Assert(result, "Expected =");
            index++;
        }

        public void AssertOpen()
        {
            bool result = (lines[index] == "{");
            Debug.Assert(result, "Expected {");
            index++;
        }

        public int CurrentIndex
        {
            get { return index; }
        }

        public string CurrentLine
        {
            get { return lines[index]; }
        }

        public bool EOF
        {
            get { return (index >= lines.Length); }
        }

        public bool GetBool()
        {
            switch (lines[index].ToUpperInvariant())
            {
                case "TRUE":
                    index++;
                    return true;
                case "FALSE":
                    index++;
                    return false;
                default:
                    return (GetInteger() != 0);
            }
        }

        public string GetCompoundString()
        {
            StringBuilder result = new StringBuilder();

            while (true)
            {
                Debug.Assert(!EOF, "End of file");

                if (lines[index] == ":" || (!IsControl(lines[index])))
                {
                    result.Append(lines[index]);
                    index++;
                }
                else
                {
                    break;
                }
            }

            return result.ToString();
        }

        public int GetInteger()
        {
            int result = 0;
            string s = lines[index];
            string alphabet = decAlphabet;
            
            if (s.StartsWith("$"))
            {
                alphabet = hexAlphabet;
                s = s.Substring(1);
            }
            else if (s.Length >= 2 && s.Substring(0, 1).ToLowerInvariant() == "0x")
            {
                alphabet = hexAlphabet;
                s = s.Substring(2);
            }

            result = GetValue(s.ToUpperInvariant(), alphabet);
            index++;

            return result;
        }

        public Offset GetOffset()
        {
            if ((index < (lines.Length - 5)) &&
                (lines[index + 0] != ":") &&
                (lines[index + 1] == ":") &&
                (lines[index + 3] == ":") &&
                (lines[index + 2] != ":") &&
                (lines[index + 4] != ":"))
            {
                // easyflash offset
                int bank = GetValue(lines[index], hexAlphabet);
                index++;
                AssertColon();
                int chip = GetValue(lines[index], hexAlphabet);
                index++;
                AssertColon();
                int offset = GetValue(lines[index], hexAlphabet);
                index++;
                return new Offset(bank, chip, offset);
            }

            return new Offset(GetInteger());
        }

        public string GetParameter()
        {
            //Debug.Assert(lines[index] == "," || lines[index] == ";" || lines[index] == ":", "Expected separator");
            string key = "";

            switch (lines[index])
            {
                case ",":
                    index++;
                    key = GetString();
                    AssertEquals();
                    break;
                case ":":
                    index++;
                    key = ":";
                    break;
                case ";":
                    break;
                default:
                    key = GetString();
                    AssertEquals();
                    break;
            }
            return key;
        }

        public string GetString()
        {
            if (!IsControl(lines[index]))
            {
                string result = lines[index];
                index++;
                return result;
            }
            else
            {
                return "";
            }
        }

        protected int GetValue(string s, string alphabet)
        {
            int result = 0;
            int size = alphabet.Length;

            foreach (char c in s)
            {
                result *= size;
                int i = alphabet.IndexOf(c);
                Debug.Assert(i >= 0, "Invalid character in value");
                result += i;
            }

            return result;
        }

        static protected bool IsControl(string s)
        {
            switch (s)
            {
                case "{":
                case "}":
                case ";":
                case "=":
                case ",":
                case ":":
                    return true;
            }
            return false;
        }

        static public Parser Parse(string[] input)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;

            foreach (string s in input)
            {
                StringBuilder line = new StringBuilder();
                bool isEscaped = false;
                bool skip = false;
                bool finished = false;
                bool comment = false;
                bool special = false;
                char quoteChar = '\0';
                int slashes = 0;
                string trimmedLine;

                foreach (char c in s)
                {
                    finished = false;
                    skip = false;
                    special = false;

                    // escaped character?
                    if (isEscaped)
                    {
                        isEscaped = false;
                        line.Append(c);
                        continue;
                    }

                    // check for escape slash
                    if (c == '\\')
                    {
                        isEscaped = true;
                        line.Append(c);
                        continue;
                    }

                    // quote character?
                    if (inQuotes)
                    {
                        if (c == quoteChar)
                            inQuotes = false;
                        else
                            line.Append(c);
                    }
                    else
                    {
                        // check for special characters
                        switch (c)
                        {
                            case '\t':
                            case ' ':
                                skip = true;
                                if (line.Length > 0)
                                    finished = true;
                                break;
                            case '{':
                            case '}':
                            case ':':
                            case ',':
                            case ';':
                                finished = true;
                                skip = true;
                                special = true;
                                break;
                            case '\'':
                            case '\"':
                                inQuotes = true;
                                quoteChar = c;
                                skip = true;
                                break;
                            case '/':
                                slashes++;
                                if (slashes == 2)
                                    comment = true;
                                skip = true;
                                break;
                        }
                        if (!skip)
                        {
                            line.Append(c);
                        }
                        else if (c != '/')
                        {
                            slashes = 0;
                        }
                    }

                    if (finished || comment)
                    {
                        trimmedLine = line.ToString().Trim();
                        if (trimmedLine.Length > 0)
                            result.Add(trimmedLine);
                        if (special)
                            result.Add(c.ToString());
                        line.Clear();
                    }

                    if (comment)
                        break;
                }

                trimmedLine = line.ToString().Trim();
                if (trimmedLine.Length > 0)
                    result.Add(trimmedLine);
            }

            Parser p = new Parser();
            p.lines = result.ToArray();
            return p;
        }
    }
}
