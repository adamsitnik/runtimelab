﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Reflection.Metadata;
using static Microsoft.ManagedZLib.ManagedZLib;
using System.Runtime.InteropServices;
//Vivi's notes> Taking out the InteropServices lib because we're not using PInvokes anymore

namespace Microsoft.ManagedZLib;

/// <summary>
/// 
/// This class is attemp to migrate ZLib to managed code. This will contain the methods that need to be 
/// written from native to a managed implemntation.
/// 
/// See also: How to choose a compression level (in comments to <code>CompressionLevel</code>.
/// </summary>
public static class ManagedZLib
{
    //Vivi's notes(ES):Es justo y necesario (tener una estructura para ZStream)
    //aunque si requiere un formato mas complejo que ints, tal vez sea necesario
    //usar la clase ManagedZLib.ZStream -- else, hay que borrarla (ta vacia so far)
    internal struct ZStream  //Aunque, checar ZLibStream para ver si los metodos de al final - luego de la clase- se pueden unificar
    {
        internal byte[] nextIn;  //Bytef    *next_in;  /* next input byte */
        internal byte[] nextOut; //Bytef    *next_out; /* next output byte should be put there */

        //Vivi's notes: This is for error messages but saw in the managed part of native implementation
        //that it's never being assign to anything
        //(To be checked later - Maybe the info is assign directly in the native part through the pointer)
        internal String msg;     //char     *msg;      /* last error message, NULL if no error */

        internal uint availIn;   //uInt     avail_in;  /* number of bytes available at next_in */
        internal uint availOut;  //uInt     avail_out; /* remaining free space at next_out */
    }
    //-------------Vivi's notes> Check, ZStream properties and definitions
    public static bool ReturnTrue => true; //This is just for the unit test example

    //public struct BufferHandle
    //{
    //    // Vivi's note> If we decide we're creating a struct
    //    // then this, instad of being public, would be like ZStream
    //    // internal and later their properties implemented in a secure class
    //    int Handle;
    //    //Vivi's notes> I think this is repetitive (We should use either Memory or byte[])
    //    //public Memory<byte> tempHandle;
    //    public byte[] Buffer;

    //    //Se ocupara struct de input
    //    //Struct de output
    //    //Estructura que representa el reemplazo de MemoryHandle que solo maneja pointers
    //    //public void Dispose();
    //}

    public enum FlushCode : int //Vivi's notes: For knowing how much and when to produce output
    {
        NoFlush = 0,
        SyncFlush = 2,
        Finish = 4,
        Block = 5
    }

    public enum ErrorCode : int //Vivi's notes: For error checking and other pointers usage (avail_tot)
    {
        Ok = 0,
        StreamEnd = 1,
        StreamError = -2,
        DataError = -3,
        MemError = -4,
        BufError = -5,
        VersionError = -6
    }

    public enum BlockType // For inflate (RFC1951 deflate format)
    {
        Uncompressed = 0,
        Static = 1,
        Dynamic = 2
    }

    // Vivi's notes> Tengo que copiar el summary de este enum
    public enum CompressionLevel : int
    {
        NoCompression = 0,
        BestSpeed = 1,
        DefaultCompression = -1,
        BestCompression = 9
    }

    /// <summary>
    /// <p><strong>From the ZLib manual:</strong></p>
    /// <p><code>CompressionStrategy</code> is used to tune the compression algorithm.<br />
    /// </summary>public enum CompressionStrategy : int
    public enum CompressionStrategy : int
    {
        DefaultStrategy = 0
    }

    /// <summary>
    /// In version 1.2.3, ZLib provides on the <code>Deflated</code>-<code>CompressionMethod</code>.
    /// </summary>
    public enum CompressionMethod : int
    {
        Deflated = 8 //Vivi's notes: Default compression method - deflate
    }
    // Raw deflate is actually the more basic format for defalte and inflate. The other ones like GZip and ZLib have a wrapper around
    // the data/deflate block.
    /// <summary>
    /// <p><strong>From the ZLib manual:</strong></p>
    /// <p>ZLib's <code>windowBits</code> parameter is the base two logarithm of the window size (the size of the history buffer).
    /// It should be in the range 8..15 for this version of the library. Larger values of this parameter result in better compression
    /// at the expense of memory usage. The default value is 15 if deflateInit is used instead.<br /></p>
    /// <strong>Note</strong>:
    /// <code>windowBits</code> can also be -8..-15 for raw deflate. In this case, -windowBits determines the window size.
    /// <code>Deflate</code> will then generate raw deflate data with no ZLib header or trailer, and will not compute an adler32 check value.<br />
    /// <p>See also: How to choose a compression level (in comments to <code>CompressionLevel</code>.</p>
    /// </summary>
    public const int Deflate_DefaultWindowBits = -15; // Legal values are 8..15 and -8..-15. 15 is the window size,
                                                      // negative val causes deflate to produce raw deflate data (no zlib header).

    /// <summary>
    /// <p><strong>From the ZLib manual:</strong></p>
    /// <p>ZLib's <code>windowBits</code> parameter is the base two logarithm of the window size (the size of the history buffer).
    /// It should be in the range 8..15 for this version of the library. Larger values of this parameter result in better compression
    /// at the expense of memory usage. The default value is 15 if deflateInit is used instead.<br /></p>
    /// </summary>
    public const int ZLib_DefaultWindowBits = 15;

    /// <summary>
    /// <p>Zlib's <code>windowBits</code> parameter is the base two logarithm of the window size (the size of the history buffer).
    /// For GZip header encoding, <code>windowBits</code> should be equal to a value between 8..15 (to specify Window Size) added to
    /// 16. The range of values for GZip encoding is therefore 24..31.
    /// <strong>Note</strong>:
    /// The GZip header will have no file name, no extra data, no comment, no modification time (set to zero), no header crc, and
    /// the operating system will be set based on the OS that the ZLib library was compiled to. <code>ZStream.adler</code>
    /// is a crc32 instead of an adler32.</p>
    /// </summary>
    public const int GZip_DefaultWindowBits = 31;

    /// <summary>
    /// <p><strong>From the ZLib manual:</strong></p>
    /// <p>The <code>memLevel</code> parameter specifies how much memory should be allocated for the internal compression state.
    /// <code>memLevel</code> = 1 uses minimum memory but is slow and reduces compression ratio; <code>memLevel</code> = 9 uses maximum
    /// memory for optimal speed. The default value is 8.</p>
    /// <p>See also: How to choose a compression level (in comments to <code>CompressionLevel</code>.</p>
    /// </summary>
    public const int Deflate_DefaultMemLevel = 8;     // Memory usage by deflate. Legal range: [1..9]. 8 is ZLib default.
                                                      // More is faster and better compression with more memory usage.
    public const int Deflate_NoCompressionMemLevel = 7;

    public const byte GZip_Header_ID1 = 31;
    public const byte GZip_Header_ID2 = 139;

    /**
     * Do not remove the nested typing of types inside of <code>System.IO.Compression.ZLibNative</code>.
     * This was done on purpose to:
     *
     * - Achieve the right encapsulation in a situation where <code>ZLibNative</code> may be compiled division-wide
     *   into different assemblies that wish to consume <code>System.IO.Compression.Native</code>. Since <code>internal</code>
     *   scope is effectively like <code>public</code> scope when compiling <code>ZLibNative</code> into a higher
     *   level assembly, we need a combination of inner types and <code>private</code>-scope members to achieve
     *   the right encapsulation.
     *
     * - Achieve late dynamic loading of <code>System.IO.Compression.Native.dll</code> at the right time.
     *   The native assembly will not be loaded unless it is actually used since the loading is performed by a static
     *   constructor of an inner type that is not directly referenced by user code.
     *
     *   In Dev12 we would like to create a proper feature for loading native assemblies from user-specified
     *   directories in order to PInvoke into them. This would preferably happen in the native interop/PInvoke
     *   layer; if not we can add a Framework level feature.
     */

    /// <summary>
    /// The <code>ZLibStreamHandle</code> could be a <code>CriticalFinalizerObject</code> rather than a
    /// <code>SafeHandleMinusOneIsInvalid</code>. This would save an <code>IntPtr</code> field since
    /// <code>ZLibStreamHandle</code> does not actually use its <code>handle</code> field.
    /// Instead it uses a <code>private ZStream zStream</code> field which is the actual handle data
    /// structure requiring critical finalization.
    /// However, we would like to take advantage if the better debugability offered by the fact that a
    /// <em>releaseHandleFailed MDA</em> is raised if the <code>ReleaseHandle</code> method returns
    /// <code>false</code>, which can for instance happen if the underlying ZLib <code>XxxxEnd</code>
    /// routines return an failure error code.
    /// </summary>
    public sealed class ZLibStreamHandle //Vivi's notes: Took off the inheritance part, I elaborate bellow on why
    {
        /// <summary>
        ///  ----------------------Vivi's notes>
        /// The pointers(zalloc,zfree,opaque - for using malloc and stuff in c++ algorithm) usually need to be initialized
        /// before being called for deflate, inflate or so.
        /// This states might not be necessary anymore (nor to inherit from SafeHandle).
        /// </summary>
        public enum State { NotInitialized, InitializedForDeflate, InitializedForInflate, Disposed } //Vivi's notes> Maybe se use para diagnostics luego

        // Vivi's notes>
        //Took of all the overrides related to pointers behavior and the state init methods.
        // Discovered all the implementations/methods were related to ZLibStreamHandle: SafeHandle
        //which is related to pointers handling.
        //In this new managed implementation PTRs are no longer be used - instead we'll use managed versions
        //This whole inheritance (class inherits from SafeHandle) might be unnecessary
        //Maybe we just need to use regular class/methods.
        //
        //I'm still considering if returning errors is the best way. Kept it for following the logic behind the original algorithm.
        // -wanted to point out my skepticism tho- (I'll erase these type of comments later on for sure)

        private ZStream _zStream;

        // ZStream properties so far
        public byte[] NextIn
        {
            get { return _zStream.nextIn; }
            set { _zStream.nextIn = value; }
        }

        public uint AvailIn
        {
            get { return _zStream.availIn; }
            set { _zStream.availIn = value; }
        }

        public byte[] NextOut
        {
            get { return _zStream.nextOut; }
            set { _zStream.nextOut = value; }
        }

        public uint AvailOut
        {
            get { return _zStream.availOut; }
            set { _zStream.availOut = value; }
        }


        //Vivi's notes: If we decide to do everything (compress and uncompress) in a class called Deflate/Inflate then this class might be
        // just for error checking or like a initial setup on the buffers.
        public ErrorCode DeflateInit2_(CompressionLevel level, int windowBits, int memLevel, CompressionStrategy strategy)
        {
            //Vivi's notes: For the compiler not to cry in the meantime
            _zStream.msg = "In the meantime - Suspect this is implemented in the native side of things";

            //This would have gone to a PInvoke
            //Vivi's notes: Kept notation for clarity
            return ErrorCode.Ok; //errorCode is an enum - this (int = 0) means Ok
        }


        public ErrorCode Deflate(FlushCode flush)
        {
            //Vivi's notes: For the compiler not to cry in the meantime
            _zStream.msg = "In the meantime - Suspect this is implemented in the native side of things";

            //This would have gone to a PInvoke
            return ErrorCode.Ok;
        }


        public ErrorCode DeflateEnd()
        {
            //Vivi's notes: For the compiler not to cry in the meantime
            _zStream.msg = "In the meantime - Suspect this is implemented in the native side of things";

            //Vivi's notes: This would have gone to a PInvoke
            return ErrorCode.Ok;
        }

        public ErrorCode InflateInit2_(int windowBits)
        {

               //ErrorCode errC = Interop.ZLib.InflateInit2_(stream, windowBits);

               //Vivi's notes: For the compiler not to cry in the meantime
               _zStream.msg = "In the meantime - Suspect this is implemented in the native side of things";

            //Vivi's notes: This would have gone to a PInvoke
            return ErrorCode.Ok;
        }


        public ErrorCode Inflate(FlushCode flush)
        {
            //Vivi's notes: For the compiler not to cry in the meantime
            _zStream.msg = "In the meantime - Suspect this is implemented in the native side of things";

            //Vivi's notes: This would have gone to a PInvoke for the native version of ZLib inflate
            return ErrorCode.Ok;
        }


        public ErrorCode InflateEnd()
        {
            //Vivi's notes: For the compiler not to cry in the meantime
            _zStream.msg = "In the meantime - Suspect this is implemented in the native side of things";
            return ErrorCode.Ok;
        }

        // This can work even after XxflateEnd().
        // Vivi's notes: No need for using Marshal methods (last version) but I'll keep the check for now
        public string GetErrorMessage() => _zStream.msg != null ? _zStream.msg! : string.Empty; //-- basic invalid str check
        
    }

    // -------------------------------Vivi's note> I'll keep the wrapper for later returning a ZStream (but "To be revaluated")
    // (ES) Posibilidad de unificacion con ZLibStream
    public static ErrorCode CreateZLibStreamForDeflate(out ZLibStreamHandle zLibStreamHandle, CompressionLevel level,
    int windowBits, int memLevel, CompressionStrategy strategy)
    {
        zLibStreamHandle = new ZLibStreamHandle(); //Vivi's note (ES)> Mega extra, aqui como que le pasa un stream con las struct que antes manejaban todos los ptrs
        //Aparte usa *out en los parametros, todo indica que era como una interfaz o wrapper para manejar el apuntador
        // No need of this I think
        return zLibStreamHandle.DeflateInit2_(level, windowBits, memLevel, strategy);
    }


    public static ErrorCode CreateZLibStreamForInflate(out ZLibStreamHandle zLibStreamHandle, int windowBits)
    {
        zLibStreamHandle = new ZLibStreamHandle();
        return zLibStreamHandle.InflateInit2_(windowBits);
    }
}
