//
// Author: Robert Tizzard
//
// Library: C# class library to implement the BitTorrent protocol.
//
// Description:
//
// Copyright 2020.
//

using System;
using System.Collections.Generic;

namespace BitTorrentLibrary
{
    public enum TorrentStatus
    {
        Started,
        Uploading,
        Downloading,
        Paused,
        Stopped
    }
    public struct TorrentDetails
    {
        public TorrentStatus status;
        public List<PeerDetails> peers;
        public UInt64 uploadedBytes;
        public UInt64 downloadedBytes;
        public UInt32 missingPiecesCount;
        public byte[] infoHash;
    }
}