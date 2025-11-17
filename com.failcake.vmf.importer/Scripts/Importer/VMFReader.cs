#region

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;

#endregion

namespace FailCake.VMF
{
    internal class VMFVariable
    {
        public string name { get; set; }
        public object value { get; set; }

        public override string ToString() {
            if (this.value is not VMFDataBlock block) return $"{this.name}";
            return $"{this.name}[{block.variables.Count}]";
        }
    }

    internal class VMFDataBlock
    {
        public string ID;
        public List<VMFVariable> variables = new List<VMFVariable>();

        public void Add(string? name, object value)
        {
            if (value != null)
            {
                this.variables.Add(new VMFVariable { name = name, value = value });
            }
        }

        public object? GetSingle(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return this.variables.Find(x => x.name == key)?.value;
        }

        public T[] GetAll<T>()
        {
            return this.variables
                .Select(x => x.value)
                .OfType<T>()
                .ToArray();
        }

        public string[] GetAllString(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return System.Array.Empty<string>();
            }

            return this.variables
                .FindAll(x => x.name == key)
                .Select(x => x.value as string)
                .OfType<string>()
                .ToArray();
        }

        public VMFDataBlock[] GetAllBlocks(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return System.Array.Empty<VMFDataBlock>();
            }

            return this.variables
                .FindAll(x => x.name == key)
                .Select(x => x.value as VMFDataBlock)
                .OfType<VMFDataBlock>()
                .ToArray();
        }

        public override string ToString()
        {
            return $"{this.ID}[{this.variables.Count}]";
        }
    }

    internal static class VMFReader
    {
        public static VMFDataBlock ReadBlockNamed(StreamReader reader)
        {
            if (reader == null || reader.EndOfStream)
            {
                return null;
            }

            string? name = reader.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                VMFDataBlock block = VMFReader.ReadBlock(reader);
                if (block != null)
                {
                    block.ID = name;
                }
                return block;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error reading VMF block '{name}': {ex.Message}");
                return null;
            }
        }

        #region PRIVATE

        private static VMFDataBlock ReadBlock(StreamReader reader, bool skipBlockCheck = false)
        {
            if (reader == null || reader.EndOfStream)
            {
                return null;
            }

            // Check for opening brace if this is a new block
            if (!skipBlockCheck)
            {
                string? blockStart = reader.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(blockStart))
                {
                    throw new UnityException("Malformed VMF block: EOF reached while expecting opening brace");
                }
                if (blockStart != "{")
                {
                    throw new UnityException($"Malformed VMF block: Expected '{{' but found '{blockStart}'");
                }
            }

            VMFDataBlock mainDataBlock = new VMFDataBlock();
            List<VMFDataBlock> stack = new List<VMFDataBlock> { mainDataBlock };

            try
            {
                do
                {
                    if (reader.EndOfStream)
                    {
                        throw new UnityException("Malformed VMF block: EOF reached while parsing");
                    }

                    string? line = reader.ReadLine()?.Trim();

                    // Handle different line types
                    if (line == null)
                    {
                        throw new UnityException("Malformed VMF block: Unexpected EOF");
                    }

                    if (line == "}")
                    {
                        if (stack.Count > 0)
                        {
                            stack.RemoveAt(stack.Count - 1);
                        }
                        continue;
                    }

                    if (line == "{")
                    {
                        stack.Add(new VMFDataBlock());
                        continue;
                    }

                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    // Skip comments
                    if (line.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Parse key-value pairs or named blocks
                    if (line.StartsWith('"'))
                    {
                        VMFReader.ParseKeyValuePair(line, stack[^1]);
                    }
                    else
                    {
                        VMFReader.ParseNamedBlock(line, reader, stack[^1]);
                    }
                } while (stack.Any());
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error parsing VMF block: {ex.Message}");
                throw;
            }

            return mainDataBlock;
        }

        private static void ParseKeyValuePair(string line, VMFDataBlock currentBlock)
        {
            try
            {
                int secondQuoteIndex = line.IndexOf('"', 1);
                if (secondQuoteIndex == -1)
                {
                    Debug.LogWarning($"Malformed key-value pair: {line}");
                    return;
                }

                string name = line.Substring(1, secondQuoteIndex - 1); // Extract key without quotes
                string value = line.Substring(secondQuoteIndex + 1).Trim();

                // Remove quotes from value if present
                if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                {
                    value = value.Substring(1, value.Length - 2);
                }

                currentBlock.Add(name, value);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error parsing key-value pair '{line}': {ex.Message}");
            }
        }

        private static void ParseNamedBlock(string blockName, StreamReader reader, VMFDataBlock currentBlock)
        {
            try
            {
                string? nextLine = reader.ReadLine()?.Trim();
                if (nextLine == "{")
                {
                    VMFDataBlock namedBlock = VMFReader.ReadBlock(reader, true);
                    if (namedBlock != null)
                    {
                        currentBlock.Add(blockName, namedBlock);
                    }
                }
                else
                {
                    throw new UnityException($"Expected '{{' after block name '{blockName}', but found '{nextLine}'");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error parsing named block '{blockName}': {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}

/*# MIT License Copyright (c) 2025 FailCake

# Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
# "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
# distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
# the following conditions:
#
# The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
# MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
# ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
# SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.*/