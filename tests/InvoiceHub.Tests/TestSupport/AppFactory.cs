using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace InvoiceHub.Tests.TestSupport;

/// <summary>The real application, in memory, on its own temporary SQLite file.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"invoicehub-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"DataSource={_dbPath}",
            ["SeedDemoData"] = "false",
            ["HttpsRedirection"] = "false",
        }));

    /// <summary>A "browser": its own cookie jar, does not follow redirects so tests can inspect them.</summary>
    public Browser NewBrowser() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
    }
}

public sealed partial class Browser(HttpClient client)
{
    public HttpClient Client { get; } = client;

    public Task<HttpResponseMessage> Get(string url) => Client.GetAsync(url);

    public async Task<string> GetHtml(string url)
    {
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Loads a page that contains the form, takes its anti-forgery token and posts the fields like a browser.</summary>
    public async Task<HttpResponseMessage> PostForm(string pageWithForm, string postUrl, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var token = await Token(pageWithForm);
        var all = new List<KeyValuePair<string, string>>(fields) { new("__RequestVerificationToken", token) };
        return await Client.PostAsync(postUrl, new FormUrlEncodedContent(all));
    }

    public async Task<string> Token(string pageWithForm)
    {
        var html = await GetHtml(pageWithForm);
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"No anti-forgery token on {pageWithForm}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public async Task Register(string email, string password = "Password123!")
    {
        var response = await PostForm("/account/register", "/account/register",
        [
            new("Email", email), new("Password", password), new("ConfirmPassword", password),
        ]);
        // Success is a redirect (signed in). A 200 would mean the form was shown again with errors.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public async Task<int> CreateClient(string name)
    {
        var response = await PostForm("/Clients/Create", "/Clients/Create", [new("Name", name)]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var list = await GetHtml("/Clients");
        // The client's row: its name cell, then (within the same row) the Edit link that carries the id.
        var match = Regex.Match(list, $@"fw-medium"">{Regex.Escape(name)}</td>.*?/Clients/Edit/(\d+)", RegexOptions.Singleline);
        Assert.True(match.Success, "Created client not found in the list");
        return int.Parse(match.Groups[1].Value);
    }

    public async Task<int> CreateInvoice(int clientId, string description = "Consulting", decimal qty = 2, decimal price = 100m, decimal vat = 17)
    {
        var response = await PostForm("/Invoices/Create", "/Invoices/Create",
        [
            new("ClientId", clientId.ToString()),
            new("IssueDate", "2026-10-01"), new("DueDate", "2026-10-15"),
            new("Lines[0].Description", description),
            new("Lines[0].Quantity", qty.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Lines[0].UnitPrice", price.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Lines[0].VatRate", vat.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        var match = Regex.Match(location, @"/Invoices/Details/(\d+)");
        Assert.True(match.Success, $"Unexpected redirect {location}");
        return int.Parse(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
