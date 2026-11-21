using System.Net;
using System.Net.Http.Json;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;
using UserEntity = Nakshatra.Shared.Models.User;
using UserService = Nakshatra.User.Service.ServiceMarker;

namespace Nakshatra.Tests;

public class AddressBookTests
{
    [Fact]
    public async Task Address_book_always_has_exactly_one_default()
    {
        using var users = TestHosts.Create<UserService>();
        using var client = users.CreateClient();
        var userId = await CreateUserAsync(client);
        var path = $"/api/users/{userId}/addresses";

        var home = await AddAsync(client, path, "Home");
        var office = await AddAsync(client, path, "Office");
        var parents = await AddAsync(client, path, "Parents", isDefault: true);
        var afterAdding = await ListAsync(client, path);
        await client.PostAsync($"{path}/{office.Id}/default", null);
        var afterSwitching = await ListAsync(client, path);
        await client.DeleteAsync($"{path}/{office.Id}");
        var afterDeleting = await ListAsync(client, path);

        Assert.True(home.IsDefault);
        Assert.False(office.IsDefault);
        Assert.Equal(new[] { "Parents", "Office", "Home" }, afterAdding.Select(a => a.FullName));
        Assert.Equal(new[] { "Parents" }, afterAdding.Where(a => a.IsDefault).Select(a => a.FullName));
        Assert.Equal(new[] { "Office" }, afterSwitching.Where(a => a.IsDefault).Select(a => a.FullName));
        // The most recently added remaining address takes over from a deleted default.
        Assert.Equal(new[] { "Parents", "Home" }, afterDeleting.Select(a => a.FullName));
        Assert.True(afterDeleting[0].IsDefault);
        Assert.Equal(parents.Id, afterDeleting[0].Id);
    }

    [Fact]
    public async Task Addresses_are_validated_and_private_to_their_owner()
    {
        using var users = TestHosts.Create<UserService>();
        using var client = users.CreateClient();
        var owner = await CreateUserAsync(client);
        var other = await CreateUserAsync(client);
        var address = await AddAsync(client, $"/api/users/{owner}/addresses", "Owner");

        var missingCity = await client.PostAsJsonAsync($"/api/users/{owner}/addresses", Request("No city") with { City = " " });
        var unknownUser = await client.PostAsJsonAsync($"/api/users/missing-{Guid.NewGuid():N}/addresses", Request("Ghost"));
        var editedByOther = await client.PutAsJsonAsync($"/api/users/{other}/addresses/{address.Id}", Request("Hijacked"));
        var deletedByOther = await client.DeleteAsync($"/api/users/{other}/addresses/{address.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, missingCity.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownUser.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editedByOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedByOther.StatusCode);
        Assert.Equal("Owner", Assert.Single(await ListAsync(client, $"/api/users/{owner}/addresses")).FullName);
    }

    private static async Task<string> CreateUserAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/users", new UserEntity
        {
            Id = $"user-{Guid.NewGuid():N}",
            Name = "Address Tester",
            Email = "addresses@nakshatra.dev",
            Persona = Persona.Customer
        }, ServiceDefaults.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserEntity>(ServiceDefaults.JsonOptions))!.Id;
    }

    private static AddressRequestBody Request(string fullName, bool isDefault = false)
        => new(fullName, "1 Residency Road", "Flat 2", "Bengaluru", "KA", "560025", "India", "+91 80 0000 0000", isDefault);

    private static async Task<Address> AddAsync(HttpClient client, string path, string fullName, bool isDefault = false)
    {
        var response = await client.PostAsJsonAsync(path, Request(fullName, isDefault));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Address>(ServiceDefaults.JsonOptions))!;
    }

    private static async Task<List<Address>> ListAsync(HttpClient client, string path)
        => (await client.GetFromJsonAsync<List<Address>>(path, ServiceDefaults.JsonOptions))!;

    private sealed record AddressRequestBody(
        string FullName, string Line1, string Line2, string City, string State, string PostalCode, string Country, string Phone, bool IsDefault);
}
