// Example usage of the Fixed-Size Database Page Structure
// This file demonstrates how to use the PageHeader and PageSerializer APIs

using SharpCoreDB.Core.File;
using System;
using System.Text;

namespace SharpCoreDB.Examples
{
    /// <summary>
    /// Example demonstrating the usage of the page structure implementation.
    /// </summary>
    public static class PageStructureExample
    {
        /// <summary>
        /// Example 1: Demonstrates how to create and write a database page.
        /// </summary>
        public static void Example1_CreateAndWritePage()
        {
            Console.WriteLine("=== Example 1: Create and Write a Page ===");
            
            // Create a header with PageType.Data and transaction ID
            var header = PageHeader.Create((byte)PageType.Data, transactionId: 1234);
            
            // Prepare some data to store
            byte[] myData = Encoding.UTF8.GetBytes("Hello, Database! This is test data.");
            Console.WriteLine($"Data to store: {Encoding.UTF8.GetString(myData)}");
            
            // Create a page using stackalloc (zero allocation)
            Span<byte> pageBuffer = stackalloc byte[4096];
            PageSerializer.CreatePage(ref header, myData, pageBuffer);
            
            Console.WriteLine($"Page created successfully!");
            Console.WriteLine($"Header size: {PageHeader.Size} bytes");
            Console.WriteLine($"Data size: {myData.Length} bytes");
            Console.WriteLine($"Page size: {pageBuffer.Length} bytes");
            Console.WriteLine($"Checksum: 0x{header.Checksum:X8}");
            Console.WriteLine();
        }
        
        /// <summary>
        /// Example 2: Demonstrates how to read and validate a database page.
        /// </summary>
        public static void Example2_ReadAndValidatePage()
        {
            Console.WriteLine("=== Example 2: Read and Validate a Page ===");
            
            // Create a test page
            var header = PageHeader.Create((byte)PageType.Index, transactionId: 5678);
            byte[] testData = Encoding.UTF8.GetBytes("Test index data");
            
            Span<byte> pageBuffer = stackalloc byte[4096];
            PageSerializer.CreatePage(ref header, testData, pageBuffer);
            
            // Validate the page
            bool isValid = PageSerializer.ValidatePage(pageBuffer);
            Console.WriteLine($"Page validation result: {isValid}");
            
            if (isValid)
            {
                // Read header using helper function
                var readHeader = PageSerializer.ReadHeader(pageBuffer);
                Console.WriteLine($"Page Type: {(PageType)readHeader.PageType}");
                Console.WriteLine($"Transaction ID: {readHeader.TransactionId}");
                Console.WriteLine($"Entry Count: {readHeader.EntryCount}");
                Console.WriteLine($"Checksum: 0x{readHeader.Checksum:X8}");
                
                // Extract data
                var data = PageSerializer.GetPageData(pageBuffer, out int dataLength);
                string content = Encoding.UTF8.GetString(data);
                Console.WriteLine($"Data content: {content}");
                Console.WriteLine($"Data length: {dataLength} bytes");
            }
            Console.WriteLine();
        }
        
        /// <summary>
        /// Example 3: Demonstrates how to write a page header only.
        /// </summary>
        public static void Example3_WriteHeaderOnly()
        {
            Console.WriteLine("=== Example 3: Write Header Only ===");
            
            // Create a header
            var header = PageHeader.Create((byte)PageType.Overflow, transactionId: 9999);
            header.EntryCount = 42;
            header.FreeSpaceOffset = 256;
            header.NextPageId = 123;
            
            // Write header to a buffer using helper function
            Span<byte> headerBuffer = stackalloc byte[PageHeader.Size];
            PageSerializer.WriteHeader(headerBuffer, header);
            
            Console.WriteLine($"Header written successfully!");
            Console.WriteLine($"Header size: {headerBuffer.Length} bytes");
            
            // Read it back using helper function
            var readHeader = PageSerializer.ReadHeader(headerBuffer);
            Console.WriteLine($"Page Type: {(PageType)readHeader.PageType}");
            Console.WriteLine($"Entry Count: {readHeader.EntryCount}");
            Console.WriteLine($"Free Space Offset: {readHeader.FreeSpaceOffset}");
            Console.WriteLine($"Next Page ID: {readHeader.NextPageId}");
            Console.WriteLine($"Transaction ID: {readHeader.TransactionId}");
            Console.WriteLine();
        }
        
        /// <summary>
        /// Example 4: Demonstrates manual integer serialization using page serializer.
        /// </summary>
        public static void Example4_IntegerSerialization()
        {
            Console.WriteLine("=== Example 4: Manual Integer Serialization ===");
            
            // Allocate buffer on stack
            Span<byte> buffer = stackalloc byte[20];
            
            // Write integers using BinaryPrimitives
            PageSerializer.WriteUInt32(buffer[0..], 0xDEADBEEF);
            PageSerializer.WriteInt64(buffer[4..], -123456789);
            PageSerializer.WriteUInt16(buffer[12..], 9999);
            PageSerializer.WriteInt32(buffer[14..], 42);
            
            Console.WriteLine("Values written to buffer");
            
            // Read them back
            uint value1 = PageSerializer.ReadUInt32(buffer[0..]);
            long value2 = PageSerializer.ReadInt64(buffer[4..]);
            ushort value3 = PageSerializer.ReadUInt16(buffer[12..]);
            int value4 = PageSerializer.ReadInt32(buffer[14..]);
            
            Console.WriteLine($"UInt32 (0x{value1:X8}): {value1}");
            Console.WriteLine($"Int64: {value2}");
            Console.WriteLine($"UInt16: {value3}");
            Console.WriteLine($"Int32: {value4}");
            Console.WriteLine();
        }
        
        /// <summary>
        /// Example 5: Demonstrates page validation with data corruption detection.
        /// </summary>
        public static void Example5_PageValidation()
        {
            Console.WriteLine("=== Example 5: Page Validation with Corruption ===");
            
            // Create a valid page
            var header = PageHeader.Create((byte)PageType.Data, transactionId: 100);
            byte[] data = Encoding.UTF8.GetBytes("Important data");
            
            Span<byte> pageBuffer = stackalloc byte[4096];
            PageSerializer.CreatePage(ref header, data, pageBuffer);
            
            // Validate original page
            bool isValid1 = PageSerializer.ValidatePage(pageBuffer);
            Console.WriteLine($"Original page valid: {isValid1}");
            
            // Corrupt the data
            pageBuffer[50] = 0xFF;
            pageBuffer[51] = 0xFF;
            
            // Validate corrupted page
            bool isValid2 = PageSerializer.ValidatePage(pageBuffer);
            Console.WriteLine($"Corrupted page valid: {isValid2}");
            
            // Restore and validate again
            PageSerializer.CreatePage(ref header, data, pageBuffer);
            bool isValid3 = PageSerializer.ValidatePage(pageBuffer);
            Console.WriteLine($"Restored page valid: {isValid3}");
            Console.WriteLine();
        }
        
        /// <summary>
        /// Example 6: Demonstrates performance characteristics of page operations.
        /// </summary>
        public static void Example6_PerformanceComparison()
        {
            Console.WriteLine("=== Example 6: Performance Characteristics ===");
            
            var header = PageHeader.Create((byte)PageType.Data, transactionId: 1);
            byte[] data = new byte[1000]; // 1KB of data
            new Random().NextBytes(data);
            
            // Measure header serialization
            Span<byte> headerBuffer = stackalloc byte[PageHeader.Size];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
            {
                PageSerializer.WriteHeader(headerBuffer, header);
            }
            sw.Stop();
            Console.WriteLine($"10,000 header writes: {sw.ElapsedMilliseconds}ms ({sw.ElapsedTicks / 10000.0:F2} ticks avg)");
            
            // Measure header deserialization
            sw.Restart();
            for (int i = 0; i < 10000; i++)
            {
                _ = PageSerializer.ReadHeader(headerBuffer);
            }
            sw.Stop();
            Console.WriteLine($"10,000 header reads: {sw.ElapsedMilliseconds}ms ({sw.ElapsedTicks / 10000.0:F2} ticks avg)");
            
            // Measure page creation
            Span<byte> pageBuffer = stackalloc byte[4096];
            sw.Restart();
            for (int i = 0; i < 1000; i++)
            {
                PageSerializer.CreatePage(ref header, data, pageBuffer);
            }
            sw.Stop();
            Console.WriteLine($"1,000 page creations: {sw.ElapsedMilliseconds}ms ({sw.ElapsedTicks / 1000.0:F2} ticks avg)");
            
            // Measure page validation
            sw.Restart();
            for (int i = 0; i < 1000; i++)
            {
                _ = PageSerializer.ValidatePage(pageBuffer);
            }
            sw.Stop();
            Console.WriteLine($"1,000 page validations: {sw.ElapsedMilliseconds}ms ({sw.ElapsedTicks / 1000.0:F2} ticks avg)");
            
            Console.WriteLine("\nNote: All operations are zero-allocation!");
            Console.WriteLine();
        }
        
        /// <summary>
        /// Example 7: Demonstrates working with different page types.
        /// </summary>
        public static void Example7_AllPageTypes()
        {
            Console.WriteLine("=== Example 7: Working with Different Page Types ===");
            
            Span<byte> pageBuffer = stackalloc byte[4096];
            
            // Test each page type
            var pageTypes = new[] 
            { 
                PageType.Data, 
                PageType.Index, 
                PageType.Overflow, 
                PageType.FreeList 
            };
            
            foreach (var pageType in pageTypes)
            {
                var header = PageHeader.Create((byte)pageType, transactionId: 1);
                byte[] data = Encoding.UTF8.GetBytes($"This is a {pageType} page");
                
                PageSerializer.CreatePage(ref header, data, pageBuffer);
                
                var readHeader = PageSerializer.ReadHeader(pageBuffer);
                var pageData = PageSerializer.GetPageData(pageBuffer, out _);
                
                Console.WriteLine($"Page Type: {pageType}");
                Console.WriteLine($"  Magic: 0x{readHeader.MagicNumber:X8}");
                Console.WriteLine($"  Version: {readHeader.Version}");
                Console.WriteLine($"  Valid: {readHeader.IsValid()}");
                Console.WriteLine($"  Data: {Encoding.UTF8.GetString(pageData)}");
                Console.WriteLine();
            }
        }
        
        /// <summary>
        /// Runs all page structure examples in sequence.
        /// </summary>
        public static void RunAllExamples()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Fixed-Size Database Page Structure Examples      ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝");
            Console.WriteLine();
            
            Example1_CreateAndWritePage();
            Example2_ReadAndValidatePage();
            Example3_WriteHeaderOnly();
            Example4_IntegerSerialization();
            Example5_PageValidation();
            Example6_PerformanceComparison();
            Example7_AllPageTypes();
            
            Console.WriteLine("╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║  All examples completed successfully!             ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝");
        }
    }
}
