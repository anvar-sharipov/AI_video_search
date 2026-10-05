namespace VMS.Core.Security;

public enum Permission
{
    ViewLiveStream,
    ViewArchive,
    SearchMetadata,
    SearchByFace,
    ManageKnownPersons,
    ExportClip,
    ManageCameras,
    ManageSystemConfig,
    ManageUsers,
    ManageRetentionPolicy,
    DeleteStandardArchive,
    DeleteImmutableArchive
}
