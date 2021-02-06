using System;

namespace TACT.Net.Root
{
    [Flags]
    public enum ContentFlags : uint
    {
        None = 0,
        F00000001 = 0x1, // seen on *.wlm files
        F00000002 = 0x2,
        F00000004 = 0x4,
        Windows = 0x8, // added in 7.2.0.23436
        MacOS = 0x10, // added in 7.2.0.23436
        LowViolence = 0x80, // many chinese models have this flag
        DoNotLoad = 0x100, // apparently client doesn't load files with this flag
        UpdatePlugin = 0x800, // only seen on UpdatePlugin files
        F00020000 = 0x20000, // new 9.0
        F00040000 = 0x40000, // new 9.0
        F00080000 = 0x80000, // new 9.0
        F00100000 = 0x100000, // new 9.0
        F00200000 = 0x200000, // new 9.0
        F00400000 = 0x400000, // new 9.0
        F00800000 = 0x800000, // new 9.0
        F02000000 = 0x2000000, // new 9.0
        F04000000 = 0x4000000, // new 9.0
        Encrypted = 0x8000000, // encrypted may be?
        NoNameHash = 0x10000000, // doesn't have name hash?
        UncommonResolution = 0x20000000, // added in 21737, used for many cinematics
        Bundle = 0x40000000, // not related to wow, used as some old overwatch hack
        NotCompressed = 0x80000000 // sounds have this flag
    }
}
