using VMS.Core.Domain;
using VMS.Core.Security;

namespace VMS.Core.Tests.Security;

public class AccessControlManagerTests
{
    private readonly AccessControlManager _sut = new();

    [Theory]
    [InlineData(UserRole.Viewer, false)]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.Admin, false)]
    [InlineData(UserRole.SuperAdmin, true)]
    public void Only_SuperAdmin_can_delete_immutable_archives(UserRole role, bool expected)
    {
        Assert.Equal(expected, _sut.HasPermission(role, Permission.DeleteImmutableArchive));
    }

    [Theory]
    [InlineData(UserRole.Viewer, false)]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.SuperAdmin, true)]
    public void Only_Admin_and_SuperAdmin_can_manage_cameras(UserRole role, bool expected)
    {
        Assert.Equal(expected, _sut.HasPermission(role, Permission.ManageCameras));
    }

    [Theory]
    [InlineData(UserRole.Viewer, false)]
    [InlineData(UserRole.Operator, false)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.SuperAdmin, true)]
    public void Only_Admin_and_SuperAdmin_can_manage_system_config(UserRole role, bool expected)
    {
        Assert.Equal(expected, _sut.HasPermission(role, Permission.ManageSystemConfig));
    }

    [Theory]
    [InlineData(UserRole.Viewer, true)]
    [InlineData(UserRole.Operator, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.SuperAdmin, true)]
    public void All_roles_can_view_live_stream(UserRole role, bool expected)
    {
        Assert.Equal(expected, _sut.HasPermission(role, Permission.ViewLiveStream));
    }

    [Theory]
    [InlineData(UserRole.Viewer, false)]
    [InlineData(UserRole.Operator, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.SuperAdmin, true)]
    public void Viewer_cannot_export_clips(UserRole role, bool expected)
    {
        Assert.Equal(expected, _sut.HasPermission(role, Permission.ExportClip));
    }

    [Theory]
    [InlineData(UserRole.Viewer, false)]
    [InlineData(UserRole.Operator, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.SuperAdmin, true)]
    public void Viewer_cannot_search_by_face(UserRole role, bool expected)
    {
        Assert.Equal(expected, _sut.HasPermission(role, Permission.SearchByFace));
    }

    [Fact]
    public void Authorize_throws_UnauthorizedAccessException_when_permission_denied()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _sut.Authorize(UserRole.Admin, Permission.DeleteImmutableArchive));
    }

    [Fact]
    public void Authorize_does_not_throw_when_permission_granted()
    {
        var exception = Record.Exception(
            () => _sut.Authorize(UserRole.SuperAdmin, Permission.DeleteImmutableArchive));

        Assert.Null(exception);
    }
}
