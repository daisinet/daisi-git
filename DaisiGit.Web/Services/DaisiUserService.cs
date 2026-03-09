using Daisi.SDK.Clients.V1.Orc;
using Daisi.Protos.V1;

namespace DaisiGit.Web.Services;

/// <summary>
/// Wraps the Daisinet AccountClient to search and look up users from the platform.
/// </summary>
public class DaisiUserService(AccountClientFactory accountClientFactory)
{
    /// <summary>
    /// Searches for users in the current account by name.
    /// </summary>
    public async Task<List<DaisiUserInfo>> SearchUsersAsync(string searchTerm, int pageSize = 20)
    {
        var client = accountClientFactory.Create();
        var response = await client.GetUsersAsync(new GetUsersRequest
        {
            Paging = new PagingInfo
            {
                SearchTerm = searchTerm,
                PageSize = pageSize,
                PageIndex = 0
            }
        });

        return response.Users.Select(u => new DaisiUserInfo
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.EmailAddress
        }).ToList();
    }

    /// <summary>
    /// Gets a specific user by ID.
    /// </summary>
    public async Task<DaisiUserInfo?> GetUserAsync(string userId)
    {
        var client = accountClientFactory.Create();
        try
        {
            var response = await client.GetUserAsync(new GetUserRequest { UserId = userId });
            if (response?.User == null) return null;
            return new DaisiUserInfo
            {
                Id = response.User.Id,
                Name = response.User.Name,
                Email = response.User.EmailAddress
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all users in the current account.
    /// </summary>
    public async Task<List<DaisiUserInfo>> GetAllUsersAsync()
    {
        var client = accountClientFactory.Create();
        var response = await client.GetUsersAsync(new GetUsersRequest
        {
            Paging = new PagingInfo { PageSize = 100, PageIndex = 0 }
        });

        return response.Users.Select(u => new DaisiUserInfo
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.EmailAddress
        }).ToList();
    }
}

/// <summary>
/// Simplified user info from Daisinet.
/// </summary>
public class DaisiUserInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
