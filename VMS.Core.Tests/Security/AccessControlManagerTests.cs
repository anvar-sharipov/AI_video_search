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

    [Fact]
    public void Guard_can_view_live_stream_but_nothing_else()
    {
        Assert.True(_sut.HasPermission(UserRole.Guard, Permission.ViewLiveStream));
        Assert.False(_sut.HasPermission(UserRole.Guard, Permission.ViewArchive));
        Assert.False(_sut.HasPermission(UserRole.Guard, Permission.SearchMetadata));
    }

    [Fact]
    public void Additional_grant_extends_the_role_baseline_for_exactly_that_permission()
    {
        var extra = new[] { Permission.SearchByFace };

        Assert.True(_sut.HasPermission(UserRole.Viewer, extra, Permission.SearchByFace));
    }

    [Fact]
    public void Additional_grant_does_not_leak_into_unrelated_permissions()
    {
        var extra = new[] { Permission.SearchByFace };

        Assert.False(_sut.HasPermission(UserRole.Viewer, extra, Permission.ManageCameras));
    }

    [Fact]
    public void No_additional_grants_still_honors_the_role_baseline()
    {
        Assert.True(_sut.HasPermission(UserRole.Viewer, [], Permission.ViewArchive));
    }

    [Fact]
    public void DeleteImmutableArchive_is_not_individually_grantable()
    {
        // The single most destructive permission in the system stays Role-only — see
        // AccessControlManager.NonGrantablePermissions and UserEndpoints.TryResolveAdditionalPermissions,
        // which is the thing that actually enforces this at the API boundary. Asserting the set's
        // contents here catches anyone removing the safeguard without noticing what it protects.
        Assert.Contains(Permission.DeleteImmutableArchive, AccessControlManager.NonGrantablePermissions);
    }

    [Fact]
    public void Authorize_with_additional_grants_throws_when_neither_role_nor_grant_covers_it()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _sut.Authorize(UserRole.Viewer, [], Permission.ManageCameras));
    }

    [Fact]
    public void Authorize_with_additional_grants_does_not_throw_when_the_grant_covers_it()
    {
        var exception = Record.Exception(
            () => _sut.Authorize(UserRole.Viewer, [Permission.SearchByFace], Permission.SearchByFace));

        Assert.Null(exception);
    }
}
