namespace PRomda.FileShare;

public sealed record LibraryInfo(string Code, string Name, string Description, string Folder);
public sealed record FileInfoDto(string Name, long Size, DateTime ModifiedUtc);
public sealed record AccessRequest(string RequestId, string LibraryCode, string RemoteName);
public sealed record DownloadRequest(string RequestId, string FileName, long Size, string RemoteName);
