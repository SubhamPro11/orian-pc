namespace Orion.Core.Models;

public enum PlatformCategory
{
    Windows,
    Android,
    Linux,
    MacOS,
    All
}

public enum SortOption
{
    Home,
    Newest,
    Oldest,
    NameAsc,
    NameDesc,
    SizeAsc,
    SizeDesc
}

public enum CpuArchitecture
{
    X64,
    Arm64,
    X86,
    Universal,
    Unknown
}

public enum PackageType
{
    Exe,
    Msi,
    Msix,
    ZipPortable,
    Apk,
    Dmg,
    AppImage,
    Deb,
    Rpm,
    Unknown
}

public enum CollectionType
{
    Hero,
    Swimlane,
    Bundle,
    RecommendationBundles,
    CategoryCards,
    SortedGrid,
    Ranked
}

