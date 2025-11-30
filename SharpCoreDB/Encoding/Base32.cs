// Michel Posseth - 2024-05-20 YYYY-MM-DD
// Encode and decode Base32 
using System.Text;
namespace SharpCoreDB.Base32Encoding
{
    public static class Base32
    {
        private const string Base32Chars = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string Encode(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (data.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder result = new(data.Length * 8 / 5 + 1);
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
                    result.Append(Base32Chars[hi >> bitsRemaining - 5 & 0x1F]);
                    bitsRemaining -= 5;
                }
            }

            if (bitsRemaining > 0)
            {
                result.Append(Base32Chars[hi << 5 - bitsRemaining & 0x1F]);
            }

            return result.ToString();
        }


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
                    output[outputIndex++] = (byte)(bits >> bitsRemaining - 8);
                    bitsRemaining -= 8;
                }
            }

            return output;
        }



    }
}