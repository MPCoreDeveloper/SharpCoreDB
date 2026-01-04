// <copyright file="Base32.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Base32Encoding
{
    using System.Text;

    /// <summary>
    /// Provides Base32 encoding and decoding functionality.
    /// </summary>
    public static class Base32
    {
        private const string Base32Chars = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>
        /// Encodes a byte array into a Base32 string.
        /// </summary>
        /// <param name="data">The byte array to encode.</param>
        /// <returns>The Base32 encoded string.</returns>
        public static string Encode(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (data.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder result = new((data.Length * 8 / 5) + 1);
            int hi = 0, bitsRemaining = 0, index = 0;

            while (index < data.Length)
            {
                if (bitsRemaining > 0)
                {
                    hi = hi << 8 | data[index++];
                    bitsRemaining += 8;
                }
                else
                {
                    hi = data[index++];
                    bitsRemaining = 8;
                }

                while (bitsRemaining >= 5)
                {
                    result.Append(Base32Chars[hi >> (bitsRemaining - 5) & 0x1F]);
                    bitsRemaining -= 5;
                }
            }

            if (bitsRemaining > 0)
            {
                result.Append(Base32Chars[hi << (5 - bitsRemaining) & 0x1F]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Decodes a Base32 string into a byte array.
        /// </summary>
        /// <param name="input">The Base32 string to decode.</param>
        /// <returns>The decoded byte array.</returns>
        public static byte[] Decode(string input)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (input.Length == 0)
            {
                return [];
            }

            input = input.ToUpperInvariant();

            byte[] output = new byte[input.Length * 5 / 8];
            int bits = 0;
            int bitsRemaining = 0;
            int outputIndex = 0;

            foreach (char c in input)
            {
                if (c < '0' || c > 'Z' || c == 'I' || c == 'L' || c == 'O')
                {
                    throw new ArgumentException("Invalid character in the input string.", nameof(input));
                }

                int value = Base32Chars.IndexOf(c);
                if (value < 0)
                {
                    throw new ArgumentException("Invalid character in the input string.", nameof(input));
                }

                bits = bits << 5 | value;
                bitsRemaining += 5;

                if (bitsRemaining >= 8)
                {
                    output[outputIndex++] = (byte)(bits >> (bitsRemaining - 8));
                    bitsRemaining -= 8;
                }
            }

            return output;
        }
    }
}
