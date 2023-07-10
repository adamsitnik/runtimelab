﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Diagnostics;

namespace Microsoft.ManagedZLib;

/// <summary>
/// This class maintains a window for decompressed output.
/// We need to keep this because the decompressed information can be
/// a literal or a length/distance pair. For length/distance pair,
/// we need to look back in the output window and copy bytes from there.
/// We use a byte array of WindowSize circularly.
/// </summary>
internal sealed class OutputBuffer
{
    private int WindowSize; //Const pa hacerlo static para crear el arreglo de bytes.
    private int WindowMask;
    // With Deflate64 we can have up to a 65536 length as well as up to a 65538 distance. This means we need a Window that is at
    // least 131074 bytes long so we have space to retrieve up to a full 64kb in lookback and place it in our buffer without
    // overwriting existing data. OutputBuffer requires that the WindowSize be an exponent of 2, so we round up to 2^18.
    //private const int WindowSize64k = 262144; // -------- 64K
    //private const int WindowMask64k = 262143; //18bit

    private byte[] _window; // The window is 2^n bytes where n is number of bits
    private int _end;       // this is the position to where we should write next byte
    private int _bytesUsed; // The number of bytes in the output window which is not consumed.

    /// <summary>
    /// Initialized of the output buffer size with the window bits given.
    /// This will recieve the window bits and turn that into a base 2 number for the actual window byte size
    /// </summary>
    internal OutputBuffer(int windowBits)
    {
        WindowSize = 2 ^ windowBits;
        WindowMask = WindowSize - 1;
        _window = new byte[WindowSize];
    }
    internal OutputBuffer() //deflate64
    {
        WindowSize = 262144;
        WindowMask = 262143;
        _window = new byte[WindowSize];
    }
    internal void ClearBytesUsed()
    {
        _bytesUsed = 0;
    }

    /// <summary>Add a byte to output window.</summary>
    public void Write(byte b)
    {
        Debug.Assert(_bytesUsed < WindowSize, "Can't add byte when window is full!");
        _window[_end++] = b;
        _end &= WindowMask;
        ++_bytesUsed;
    }

    public void WriteLengthDistance(int length, int distance)
    {
        Debug.Assert((_bytesUsed + length) <= WindowSize, "No Enough space");

        // move backwards distance bytes in the output stream,
        // and copy length bytes from this position to the output stream.
        _bytesUsed += length;
        int copyStart = (_end - distance) & WindowMask; // start position for coping.

        int border = WindowSize - length;
        if (copyStart <= border && _end < border)
        {
            if (length <= distance)
            {
                Array.Copy(_window, copyStart, _window, _end, length);
                _end += length;
            }
            else
            {
                // The referenced string may overlap the current
                // position; for example, if the last 2 bytes decoded have values
                // X and Y, a string reference with <length = 5, distance = 2>
                // adds X,Y,X,Y,X to the output stream.
                while (length-- > 0)
                {
                    _window[_end++] = _window[copyStart++];
                }
            }
        }
        else
        {
            // copy byte by byte
            while (length-- > 0)
            {
                _window[_end++] = _window[copyStart++];
                _end &= WindowMask;
                copyStart &= WindowMask;
            }
        }
    }

    /// <summary>
    /// Copy up to length of bytes from input directly.
    /// This is used for uncompressed block.
    /// </summary>
    public int CopyFrom(InputBuffer input, int length)
    {
        length = Math.Min(Math.Min(length, WindowSize - _bytesUsed), input.AvailableBytes);
        int copied;

        // We might need wrap around to copy all bytes.
        int tailLen = WindowSize - _end;
        if (length > tailLen)
        {
            // copy the first part
            copied = input.CopyTo(_window, _end, tailLen);
            if (copied == tailLen)
            {
                // only try to copy the second part if we have enough bytes in input
                copied += input.CopyTo(_window, 0, length - tailLen);
            }
        }
        else
        {
            // only one copy is needed if there is no wrap around.
            copied = input.CopyTo(_window, _end, length);
        }

        _end = (_end + copied) & WindowMask;
        _bytesUsed += copied;
        return copied;
    }

    /// <summary>Free space in output window.</summary>
    public int FreeBytes => WindowSize - _bytesUsed;

    /// <summary>Bytes not consumed in output window.</summary>
    public int AvailableBytes => _bytesUsed;

    // ReadInflateOutput
    /// <summary>Copy the decompressed bytes to output buffer.</summary>
    public int CopyTo(Span<byte> output)
    {
        int copy_end;

        if (output.Length > _bytesUsed)
        {
            // we can copy all the decompressed bytes out
            copy_end = _end;
            output = output.Slice(0, _bytesUsed);
        }
        else
        {
            copy_end = (_end - _bytesUsed + output.Length) & WindowMask; // copy length of bytes
        }

        int copied = output.Length;

        int tailLen = output.Length - copy_end;
        if (tailLen > 0)
        {
            // this means we need to copy two parts separately
            // copy the taillen bytes from the end of the output window
            _window.AsSpan(WindowSize - tailLen, tailLen).CopyTo(output);
            output = output.Slice(tailLen, copy_end);
        }
        _window.AsSpan(copy_end - output.Length, output.Length).CopyTo(output);
        _bytesUsed -= copied;
        Debug.Assert(_bytesUsed >= 0, "check this function and find why we copied more bytes than we have");
        return copied;
    }
}

