﻿//
// Author: Rob Tizzard
//
// Library: C# class library to implement the BitTorrent protocol.
//
// Description: Details associated with each file in a torrent to download.
//
// Copyright 2020.
//
using System;
namespace BitTorrentLibrary
{
    internal struct FileDetails
    {
        public string name;     // File file name path
        public UInt64 length;   // File length in bytes
        public string md5sum;   // Checksum for file (optional)
        public UInt64 offset;   // Offset within torrent stream of file start
    }
}
