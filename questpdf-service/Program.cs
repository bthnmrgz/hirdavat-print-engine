using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static PdfHelpers;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
var apiKey = builder.Configuration["QUESTPDF_API_KEY"];
var allowedOrigins = SplitCsv(builder.Configuration["ALLOWED_ORIGINS"]);
var pdfRetention = RetentionFrom(builder.Configuration["PDF_RETENTION_HOURS"], 24);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
builder.Services.AddHttpClient("logo", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient("catalog-image", client =>
{
    client.Timeout = TimeSpan.FromSeconds(4);
});

var app = builder.Build();
var generatedPdfDirectory = Path.Combine(app.Environment.ContentRootPath, "generated-pdfs");

Directory.CreateDirectory(generatedPdfDirectory);
CleanupGeneratedPdfs(generatedPdfDirectory, pdfRetention, app.Logger);

app.UseCors();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(generatedPdfDirectory),
    RequestPath = "/files"
});

app.MapGet("/health", () => Results.Ok(new { ok = true, service = "hirdavat-questpdf" }));

app.MapPost("/render/order-slip", async Task<IResult> (IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    var authError = RequireApiKey(httpContext, apiKey);
    if (authError is not null)
        return authError;

    var payloadResult = await ReadPayloadAsync(httpContext);
    if (payloadResult.Error is not null)
        return payloadResult.Error;

    var payload = payloadResult.Payload!;
    var error = Validate(payload);

    if (!string.IsNullOrWhiteSpace(error))
        return Results.BadRequest(error);

    byte[] pdf;

    try
    {
        var logoBytes = await TryFetchLogoAsync(payload.Company?.LogoUrl, httpClientFactory);
        pdf = GeneratePrintPdf(payload, logoBytes);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Failed to render PDF for {DocumentType} {DocumentNo}", payload.DocumentType, payload.DocumentNo);
        return Results.Problem("PDF render edilemedi.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var fileName = SafeFileName(payload.DocumentNo, "order-slip") + ".pdf";
    return Results.File(pdf, "application/pdf", fileName);
});

app.MapPost("/render/order-slip-url", async Task<IResult> (IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    var authError = RequireApiKey(httpContext, apiKey);
    if (authError is not null)
        return authError;

    var payloadResult = await ReadPayloadAsync(httpContext);
    if (payloadResult.Error is not null)
        return payloadResult.Error;

    var payload = payloadResult.Payload!;
    var error = Validate(payload);

    if (!string.IsNullOrWhiteSpace(error))
        return Results.BadRequest(new
        {
            ok = false,
            error
        });

    byte[] pdf;

    try
    {
        var logoBytes = await TryFetchLogoAsync(payload.Company?.LogoUrl, httpClientFactory);
        pdf = GeneratePrintPdf(payload, logoBytes);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Failed to render PDF for {DocumentType} {DocumentNo}", payload.DocumentType, payload.DocumentNo);
        return Results.Json(new
        {
            ok = false,
            error = new
            {
                code = "render_failed",
                message = "PDF render edilemedi.",
                detail = exception.Message
            }
        }, statusCode: StatusCodes.Status500InternalServerError);
    }

    var fileName = UniquePdfFileName(payload.DocumentNo, UrlFileNameFallback(payload));
    var filePath = Path.Combine(generatedPdfDirectory, fileName);

    CleanupGeneratedPdfs(generatedPdfDirectory, pdfRetention, app.Logger);
    await File.WriteAllBytesAsync(filePath, pdf);

    var publicUrl = PublicUrl(httpContext, "/files/" + Uri.EscapeDataString(fileName));

    return Results.Ok(new
    {
        ok = true,
        pdf_url = publicUrl,
        file_name = fileName,
        content_type = "application/pdf",
        size_bytes = pdf.Length
    });
});

app.MapPost("/render/catalog-price-list-url", async Task<IResult> (IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    var authError = RequireApiKey(httpContext, apiKey);
    if (authError is not null)
        return authError;

    var payloadResult = await ReadPayloadAsync(httpContext);
    if (payloadResult.Error is not null)
        return payloadResult.Error;

    var payload = payloadResult.Payload!;
    if (!Text(payload.DocumentType).Equals("catalog_price_list", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            ok = false,
            success = false,
            error = "document_type 'catalog_price_list' olmalidir."
        });
    }

    var error = ValidateCatalogPriceList(payload);

    if (!string.IsNullOrWhiteSpace(error))
    {
        return Results.BadRequest(new
        {
            ok = false,
            success = false,
            error
        });
    }

    byte[] pdf;

    try
    {
        var logoBytes = await TryFetchLogoAsync(payload.Company?.LogoUrl, httpClientFactory);
        var productImages = await TryFetchCatalogProductImagesAsync(payload, httpClientFactory, app.Logger);
        pdf = GenerateCatalogPriceListPdf(payload, logoBytes, productImages);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Failed to render catalog PDF for {DocumentNo}", payload.DocumentNo);
        return Results.Json(new
        {
            ok = false,
            success = false,
            error = new
            {
                code = "render_failed",
                message = "Katalog PDF render edilemedi.",
                detail = exception.Message
            }
        }, statusCode: StatusCodes.Status500InternalServerError);
    }

    var fileName = UniquePdfFileName(payload.DocumentNo, "catalog-price-list");
    var filePath = Path.Combine(generatedPdfDirectory, fileName);

    CleanupGeneratedPdfs(generatedPdfDirectory, pdfRetention, app.Logger);
    await File.WriteAllBytesAsync(filePath, pdf);

    var publicUrl = PublicUrl(httpContext, "/files/" + Uri.EscapeDataString(fileName));

    return Results.Ok(new
    {
        ok = true,
        success = true,
        pdf_url = publicUrl,
        url = publicUrl,
        documentNo = payload.DocumentNo,
        document_no = payload.DocumentNo,
        file_name = fileName,
        content_type = "application/pdf",
        size_bytes = pdf.Length
    });
});

app.Run();

static IResult? RequireApiKey(HttpContext httpContext, string? configuredApiKey)
{
    if (string.IsNullOrWhiteSpace(configuredApiKey))
        return Results.Problem("QUESTPDF_API_KEY is not configured.", statusCode: StatusCodes.Status500InternalServerError);

    var requestApiKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();

    if (!string.Equals(requestApiKey, configuredApiKey, StringComparison.Ordinal))
        return Results.Json(new
        {
            ok = false,
            error = new
            {
                code = "unauthorized",
                message = "X-Api-Key hatali veya eksik."
            }
        }, statusCode: StatusCodes.Status401Unauthorized);

    return null;
}

static async Task<(PrintDocumentPayload? Payload, IResult? Error)> ReadPayloadAsync(HttpContext httpContext)
{
    using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
    var rawBody = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(rawBody))
        return (null, InvalidPayloadError("empty_body", "Request body bos veya JSON olarak okunamadi."));

    if (WantsLabelerLinesPayload(httpContext))
        return ReadLabelerLinesPayload(httpContext, rawBody);

    try
    {
        return ParsePayload(rawBody);
    }
    catch (JsonException exception)
    {
        return (null, InvalidPayloadError("invalid_json", "Request body gecerli JSON olmali.", exception.Message));
    }
}

static (PrintDocumentPayload? Payload, IResult? Error) ParsePayload(string body)
{
    var payload = JsonSerializer.Deserialize<PrintDocumentPayload>(body);

    if (payload is null)
        return (null, InvalidPayloadError("empty_body", "Request body bos veya JSON olarak okunamadi."));

    return (payload, null);
}

static (PrintDocumentPayload? Payload, IResult? Error) ReadLabelerLinesPayload(HttpContext httpContext, string rawBody)
{
    var rowDelimiter = NormalizeDelimiter(FirstRequestText(httpContext, "row_delimiter", "X-Row-Delimiter"), "\n");
    var fieldDelimiter = NormalizeDelimiter(FirstRequestText(httpContext, "field_delimiter", "X-Field-Delimiter"), "__FIELD__");
    var rows = SplitRawRows(rawBody, rowDelimiter);

    if (rows.Count == 0)
        return (null, InvalidPayloadError("invalid_raw", "Raw labeler body en az bir satir icermelidir."));

    var labels = new List<LabelerItem>();

    for (var index = 0; index < rows.Count; index++)
    {
        var parts = rows[index].Split(fieldDelimiter, 3, StringSplitOptions.None);
        var svgCode = DecodeHtmlEntities(Text(parts.ElementAtOrDefault(0)));
        var stockName = DecodeHtmlEntities(Text(parts.ElementAtOrDefault(1)));
        var countText = Text(parts.ElementAtOrDefault(2));

        if (string.IsNullOrWhiteSpace(svgCode))
            return (null, InvalidPayloadError("invalid_raw", $"raw satir[{index + 1}] SVG icermelidir."));

        var labelCount = 1;

        if (!string.IsNullOrWhiteSpace(countText)
            && (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out labelCount) || labelCount < 1))
        {
            return (null, InvalidPayloadError("invalid_raw", $"raw satir[{index + 1}] adet degeri 1 veya daha buyuk sayi olmalidir."));
        }

        labels.Add(new LabelerItem
        {
            SvgCode = svgCode,
            StockName = stockName,
            LabelCount = labelCount
        });
    }

    return (new PrintDocumentPayload
    {
        DocumentType = "labeler",
        DocumentNo = FirstRequestText(httpContext, "document_no", "documentNo", "X-Document-No"),
        Labels = labels
    }, null);
}

static bool WantsLabelerLinesPayload(HttpContext httpContext)
{
    var mode = Text(FirstRequestText(httpContext, "body_mode", "X-QuestPDF-Body-Mode"));

    return mode.Equals("labeler_lines", StringComparison.OrdinalIgnoreCase)
        || mode.Equals("labeler-lines", StringComparison.OrdinalIgnoreCase);
}

static string? FirstRequestText(HttpContext httpContext, params string[] names)
{
    foreach (var name in names)
    {
        var queryValue = Text(httpContext.Request.Query[name].FirstOrDefault());

        if (queryValue.Length > 0)
            return DecodeHtmlEntities(queryValue);

        var headerValue = Text(httpContext.Request.Headers[name].FirstOrDefault());

        if (headerValue.Length > 0)
            return DecodeHtmlEntities(headerValue);
    }

    return null;
}

static string NormalizeDelimiter(string? value, string fallback)
{
    var delimiter = Text(value);

    if (delimiter.Length == 0)
        delimiter = fallback;

    return delimiter
        .Replace("\\r\\n", "\n", StringComparison.Ordinal)
        .Replace("\\n", "\n", StringComparison.Ordinal)
        .Replace("\\r", "\n", StringComparison.Ordinal)
        .Replace("\\t", "\t", StringComparison.Ordinal);
}

static List<string> SplitRawRows(string rawBody, string delimiter)
{
    var text = DecodeHtmlEntities(rawBody).Trim();

    if (text.Length == 0)
        return [];

    var rows = delimiter == "\n"
        ? text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : text.Split(delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    return rows
        .Where(row => row.Length > 0)
        .ToList();
}

static string DecodeHtmlEntities(string value) => WebUtility.HtmlDecode(value) ?? "";

static IResult InvalidPayloadError(string code, string message, string? detail = null)
{
    return Results.Json(new
    {
        ok = false,
        error = new
        {
            code,
            message,
            detail
        }
    }, statusCode: StatusCodes.Status400BadRequest);
}

static string[] SplitCsv(string? value)
{
    return Text(value)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static TimeSpan? RetentionFrom(string? value, int defaultHours)
{
    if (int.TryParse(Text(value), out var hours))
    {
        if (hours <= 0)
            return null;

        return TimeSpan.FromHours(hours);
    }

    return TimeSpan.FromHours(defaultHours);
}

static void CleanupGeneratedPdfs(string directory, TimeSpan? retention, ILogger logger)
{
    if (retention is null || !Directory.Exists(directory))
        return;

    var cutoff = DateTimeOffset.UtcNow.Subtract(retention.Value);

    foreach (var filePath in Directory.EnumerateFiles(directory, "*.pdf"))
    {
        try
        {
            if (File.GetLastWriteTimeUtc(filePath) < cutoff.UtcDateTime)
                File.Delete(filePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to clean generated PDF {FilePath}", filePath);
        }
    }
}

static string Validate(PrintDocumentPayload payload)
{
    var documentType = Text(payload.DocumentType);

    if (!IsAllowedDocumentType(documentType))
        return "document_type 'quote', 'receipt', 'order_slip', 'cari_ledger' veya 'labeler' olmalidir.";

    if (documentType == "labeler")
        return ValidateLabeler(payload);

    if (documentType == "cari_ledger")
    {
        if (string.IsNullOrWhiteSpace(payload.Cari?.Name))
            return "cari.name zorunludur.";

        if (payload.Columns is not { Count: > 0 })
            return "columns alani en az bir kolon icermelidir.";

        foreach (var column in payload.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Key))
                return "columns icindeki key alanlari zorunludur.";
        }

        return "";
    }

    if (payload.Table is not null)
    {
        if (payload.Table.Columns is not { Count: > 0 })
            return "table.columns alani en az bir kolon icermelidir.";

        foreach (var column in payload.Table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Key))
                return "table.columns icindeki key alanlari zorunludur.";
        }
    }

    if (string.IsNullOrWhiteSpace(payload.Company?.Name))
        return "company.name zorunludur.";

    if (documentType == "receipt")
    {
        if (payload.Payments is null && payload.Table is null)
            return "payments alani liste olarak veya table alani tablo olarak gonderilmelidir.";
    }
    else if (payload.Items is null && payload.Table is null)
    {
        return "items alani liste olarak veya table alani tablo olarak gonderilmelidir.";
    }

    return "";
}

static bool IsAllowedDocumentType(string documentType)
{
    return documentType is "quote" or "receipt" or "order_slip" or "cari_ledger" or "labeler";
}

static string ValidateLabeler(PrintDocumentPayload payload)
{
    if (payload.Labels is not { Count: > 0 })
        return "labels alani en az bir etiket icermelidir.";

    for (var index = 0; index < payload.Labels.Count; index++)
    {
        var label = payload.Labels[index];
        var itemNo = index + 1;

        if (string.IsNullOrWhiteSpace(label.SvgCode))
            return $"labels[{itemNo}].svg_kod zorunludur.";

        if (!IsValidSvgCode(label.SvgCode))
            return $"labels[{itemNo}].svg_kod gecerli SVG olmalidir.";

        if (label.LabelCount < 1)
            return $"labels[{itemNo}].etiket_adedi 1 veya daha buyuk olmalidir.";
    }

    return "";
}

static string ValidateCatalogPriceList(PrintDocumentPayload payload)
{
    if (payload.CatalogPages is not { Count: > 0 })
        return "pages alani en az bir katalog sayfasi icermelidir.";

    if (payload.CatalogPages.Count > 120)
        return "pages alani en fazla 120 sayfa icerebilir.";

    var productCount = 0;
    var contentPageCount = 0;
    var maxProductsPerPage = Math.Clamp(payload.CatalogSummary?.ItemsPerPage ?? 16, 1, 32);

    for (var index = 0; index < payload.CatalogPages.Count; index++)
    {
        var page = payload.CatalogPages[index];
        var pageNo = index + 1;
        var type = Text(page.Type).ToLowerInvariant();

        if (type is not ("cover" or "section" or "products"))
            return $"pages[{pageNo}].type cover, section veya products olmalidir.";

        var hasGeneratedImage = !string.IsNullOrWhiteSpace(page.GeneratedImage?.DataUri);
        var pageProducts = page.Products?
            .Where(product => HasCatalogProductContent(product))
            .ToList() ?? [];

        if (type is "section" or "products")
        {
            if (pageProducts.Count == 0 && !hasGeneratedImage)
                return $"pages[{pageNo}].products en az bir urun icermelidir.";

            if (pageProducts.Count > maxProductsPerPage)
                return $"pages[{pageNo}].products en fazla {maxProductsPerPage} urun icermelidir.";

            contentPageCount++;
        }

        var imageError = ValidateCatalogGeneratedImage(page.GeneratedImage, pageNo);
        if (!string.IsNullOrWhiteSpace(imageError))
            return imageError;

        productCount += pageProducts.Count;

        if (productCount > 5000)
            return "pages icindeki toplam urun sayisi en fazla 5000 olabilir.";
    }

    if (contentPageCount == 0 || productCount == 0)
        return "katalog en az bir urun sayfasi ve urun icermelidir.";

    return "";
}

static bool HasCatalogProductContent(CatalogProduct? product)
{
    if (product is null)
        return false;

    return !string.IsNullOrWhiteSpace(FirstNonEmpty(
        product.Name,
        product.Sku,
        product.CatalogCode,
        product.PriceDisplay));
}

static string ValidateCatalogGeneratedImage(CatalogGeneratedImage? image, int pageNo)
{
    var dataUri = Text(image?.DataUri);

    if (dataUri.Length == 0)
        return "";

    var mimeType = CatalogImageMimeFromDataUri(dataUri);
    if (!IsAllowedCatalogImageMime(mimeType))
        return $"pages[{pageNo}].generated_image desteklenen data URI image/png, image/jpeg veya image/webp olmalidir.";

    var base64 = CatalogImageBase64FromDataUri(dataUri);
    if (base64.Length == 0)
        return $"pages[{pageNo}].generated_image base64 icerik icermelidir.";

    int byteLength;
    try
    {
        byteLength = EstimateCatalogBase64ByteLength(base64);
    }
    catch (FormatException)
    {
        return $"pages[{pageNo}].generated_image gecerli base64 olmali.";
    }

    if (image?.ByteLength is > 0)
        byteLength = image.ByteLength.Value;

    if (byteLength > 8 * 1024 * 1024)
        return $"pages[{pageNo}].generated_image en fazla 8 MB olabilir.";

    try
    {
        _ = Convert.FromBase64String(base64);
    }
    catch (FormatException)
    {
        return $"pages[{pageNo}].generated_image gecerli base64 olmali.";
    }

    return "";
}

static async Task<byte[]?> TryFetchLogoAsync(string? logoUrl, IHttpClientFactory httpClientFactory)
{
    if (!Uri.TryCreate(Text(logoUrl), UriKind.Absolute, out var uri))
        return null;

    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        return null;

    if (LooksLikeSvgText(uri.AbsolutePath))
        return null;

    try
    {
        var client = httpClientFactory.CreateClient("logo");
        using var response = await client.GetAsync(uri);

        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (LooksLikeSvgText(contentType))
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return LooksLikeSvgBytes(bytes) ? null : bytes;
    }
    catch
    {
        return null;
    }
}

static async Task<IReadOnlyDictionary<string, byte[]>> TryFetchCatalogProductImagesAsync(
    PrintDocumentPayload payload,
    IHttpClientFactory httpClientFactory,
    ILogger logger)
{
    var urls = (payload.CatalogPages ?? [])
        .SelectMany(page => page.Products ?? [])
        .Select(product => Text(product.ImageUrl))
        .Where(IsFetchableRasterImageUrl)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(120)
        .ToList();

    if (urls.Count == 0)
        return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    var client = httpClientFactory.CreateClient("catalog-image");
    var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    using var throttler = new SemaphoreSlim(8);

    var tasks = urls.Select(async imageUrl =>
    {
        await throttler.WaitAsync();
        try
        {
            var bytes = await TryFetchRasterImageAsync(imageUrl, client);
            if (bytes is null)
                return;

            lock (images)
                images[imageUrl] = bytes;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Skipping catalog product image {ImageUrl}", imageUrl);
        }
        finally
        {
            throttler.Release();
        }
    });

    await Task.WhenAll(tasks);
    return images;
}

static bool IsFetchableRasterImageUrl(string? imageUrl)
{
    if (!Uri.TryCreate(Text(imageUrl), UriKind.Absolute, out var uri))
        return false;

    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        return false;

    return !LooksLikeSvgText(uri.AbsolutePath);
}

static async Task<byte[]?> TryFetchRasterImageAsync(string imageUrl, HttpClient client)
{
    if (!Uri.TryCreate(Text(imageUrl), UriKind.Absolute, out var uri))
        return null;

    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
    if (!response.IsSuccessStatusCode)
        return null;

    var contentType = response.Content.Headers.ContentType?.MediaType;
    if (LooksLikeSvgText(contentType))
        return null;

    if (!string.IsNullOrWhiteSpace(contentType) && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        return null;

    const long maxImageBytes = 1_500_000;
    if (response.Content.Headers.ContentLength is > maxImageBytes)
        return null;

    var bytes = await response.Content.ReadAsByteArrayAsync();
    if (bytes.Length == 0 || bytes.Length > maxImageBytes)
        return null;

    return LooksLikeSvgBytes(bytes) ? null : bytes;
}

static byte[] GeneratePrintPdf(PrintDocumentPayload payload, byte[]? logoBytes)
{
    if (Text(payload.DocumentType) == "labeler")
        return new PrintDocumentPdfDocument(payload, logoBytes).GeneratePdf();

    var pdf = new PrintDocumentPdfDocument(payload, logoBytes, showPageNumbers: false).GeneratePdf();

    return CountPdfPages(pdf) > 1
        ? new PrintDocumentPdfDocument(payload, logoBytes, showPageNumbers: true).GeneratePdf()
        : pdf;
}

static byte[] GenerateCatalogPriceListPdf(
    PrintDocumentPayload payload,
    byte[]? logoBytes,
    IReadOnlyDictionary<string, byte[]> productImages)
{
    return new CatalogPriceListPdfDocument(payload, logoBytes, productImages).GeneratePdf();
}

static int CountPdfPages(byte[] pdf)
{
    const string marker = "/Type /Page";
    var text = Encoding.ASCII.GetString(pdf);
    var count = 0;
    var index = 0;

    while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
    {
        var afterMarker = index + marker.Length;

        if (afterMarker >= text.Length || text[afterMarker] != 's')
            count++;

        index = afterMarker;
    }

    return Math.Max(count, 1);
}

static bool LooksLikeSvgText(string? value)
{
    var text = Text(value).ToLowerInvariant();
    return text.EndsWith(".svg", StringComparison.Ordinal) || text.Contains("image/svg", StringComparison.Ordinal);
}

static bool LooksLikeSvgBytes(byte[] bytes)
{
    var length = Math.Min(bytes.Length, 512);
    var text = System.Text.Encoding.UTF8.GetString(bytes, 0, length).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

    return text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
}

static bool LooksLikeSvgContent(string? value)
{
    var text = NormalizeSvgCode(value);

    return text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
}

static bool IsValidSvgCode(string? value)
{
    var text = NormalizeSvgCode(value);

    if (!LooksLikeSvgContent(text))
        return false;

    try
    {
        _ = SvgImage.FromText(text);
        return true;
    }
    catch
    {
        return false;
    }
}

static string UniquePdfFileName(string? documentNo, string fallback)
{
    var baseName = SafeFileName(documentNo, fallback);
    return baseName + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N")[..8] + ".pdf";
}

static string UrlFileNameFallback(PrintDocumentPayload payload)
{
    return Text(payload.DocumentType) switch
    {
        "cari_ledger" => "cari-ledger",
        "labeler" => "labeler",
        _ => "order-slip"
    };
}

static string PublicUrl(HttpContext httpContext, string path)
{
    var request = httpContext.Request;
    var proto = FirstHeader(request, "X-Forwarded-Proto", request.Scheme);
    var host = FirstHeader(request, "X-Forwarded-Host", request.Host.Value);

    return proto + "://" + host + path;
}

static string FirstHeader(HttpRequest request, string name, string fallback)
{
    var value = request.Headers[name].FirstOrDefault();
    return string.IsNullOrWhiteSpace(value) ? fallback : value.Split(',')[0].Trim();
}

sealed class PrintDocumentPdfDocument : IDocument
{
    private const double LabelerLabelWidthMm = 70;
    private const double LabelerLabelHeightMm = 37;
    private const double LabelerQrSizeMm = 32;
    private const double LabelerCellPaddingMm = 1;
    private const double PageNumberFooterHeightMm = 6;
    private const int LabelerColumns = 3;
    private const int LabelerRows = 8;
    private const int LabelerLabelsPerPage = LabelerColumns * LabelerRows;

    private readonly PrintDocumentPayload _payload;
    private readonly byte[]? _logoBytes;
    private readonly NormalizedPrintStyle _style;
    private readonly bool _showPageNumbers;

    public PrintDocumentPdfDocument(PrintDocumentPayload payload, byte[]? logoBytes, bool showPageNumbers = false)
    {
        _payload = payload;
        _logoBytes = logoBytes;
        _style = NormalizedPrintStyle.From(payload.PrintStyle);
        _showPageNumbers = showPageNumbers;
    }

    private string DocumentType => Text(_payload.DocumentType);

    private bool HasCustomer => HasPartyContent(_payload.Customer);

    private double HeaderBodyGapMm => DocumentType == "cari_ledger"
        ? 8
        : _style.HeaderGapMm;

    private string DefaultTitle => DocumentType switch
    {
        "cari_ledger" => "Cari Ekstresi",
        "quote" => "Teklif",
        "receipt" => "Makbuz",
        _ => "Sipariş Fişi"
    };

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        if (DocumentType == "labeler")
        {
            ComposeLabelerDocument(container);
            return;
        }

        container.Page(page =>
        {
            page.Size(_style.PaperSize);
            page.Margin(Mm(_style.PageMarginMm));
            page.DefaultTextStyle(text => text.FontSize((float)_style.BodyFontPx).FontColor(Colors.Grey.Darken4));

            page.Header()
                .PaddingBottom(Mm(HeaderBodyGapMm))
                .Element(ComposeHeader);

            page.Content()
                .Column(column =>
                {
                    column.Item().Element(ComposeBody);

                    if (!string.IsNullOrWhiteSpace(_payload.Order?.Note))
                    {
                        column.Item()
                            .PaddingTop(Mm(8))
                            .Text(Text(_payload.Order.Note));
                    }
                });

            page.Footer().Element(ComposePageNumberFooter);
        });
    }

    private void ComposePageNumberFooter(IContainer container)
    {
        var footer = container
            .Height(Mm(PageNumberFooterHeightMm))
            .AlignCenter()
            .AlignMiddle()
            .DefaultTextStyle(text => text
                .FontSize((float)Math.Max(_style.BodyFontPx - 0.4, 5.5))
                .FontColor(Colors.Grey.Darken1));

        if (!_showPageNumbers)
        {
            footer.Text("");
            return;
        }

        footer.Text(text =>
        {
            text.Span("Sayfa ");
            text.CurrentPageNumber();
            text.Span("/");
            text.TotalPages();
        });
    }

    private void ComposeHeader(IContainer container)
    {
        if (DocumentType == "cari_ledger")
        {
            container.Row(row =>
            {
                row.RelativeItem().Element(ComposeCariLedgerSummary);
                row.ConstantItem(Mm(_style.HeaderMetaWidthMm)).AlignRight().Element(ComposeMeta);
            });
            return;
        }

        container.Row(row =>
        {
            row.RelativeItem(_style.CompanyColumnWeight).Element(ComposeCompany);

            if (HasCustomer)
                row.RelativeItem(_style.CustomerColumnWeight).Element(ComposeCustomer);

            row.ConstantItem(Mm(_style.HeaderMetaWidthMm)).AlignRight().Element(ComposeMeta);
        });
    }

    private void ComposeCompany(IContainer container)
    {
        container.Column(column =>
        {
            void AddText(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    column.Item().Text(Text(value));
            }

            column.Spacing(Mm(_style.HeaderLineGapMm));

            if (_logoBytes is not null)
            {
                column.Item()
                    .Width(Mm(_style.LogoWidthMm))
                    .Height(Mm(_style.LogoHeightMm))
                    .Image(_logoBytes)
                    .FitArea();
            }

            if (_logoBytes is null)
                column.Item().Text(Text(_payload.Company?.Name)).Bold();

            AddText(_payload.Company?.Address);
            AddText(JoinNonEmpty(" - ", _payload.Company?.Phone, _payload.Company?.Email));
        });
    }

    private void ComposeCustomer(IContainer container)
    {
        container.BorderLeft(Mm(0.35))
            .BorderColor(Colors.Grey.Darken2)
            .PaddingLeft(Mm(_style.CustomerBlockPaddingLeftMm))
            .Column(column =>
        {
            void AddText(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    column.Item().Text(Text(value));
            }

            void AddLabelValue(string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                column.Item().Row(row =>
                {
                    row.ConstantItem(Mm(_style.CustomerLabelWidthMm)).Text(label);
                    row.RelativeItem().Text(Text(value));
                });
            }

            column.Spacing(Mm(_style.HeaderLineGapMm));
            column.Item().Text("Müşteri Bilgileri").FontColor(Colors.Grey.Darken1);

            if (!string.IsNullOrWhiteSpace(_payload.Customer?.Name))
                column.Item().Text(Text(_payload.Customer?.Name)).Bold();

            AddText(_payload.Customer?.Address);
            AddLabelValue("Tel:", _payload.Customer?.Phone);
            AddLabelValue("Mükellef Tipi:", _payload.Customer?.DocumentType);
            AddLabelValue("Nakliyat Yöntemi:", _payload.Order?.ShippingMethod);
        });
    }

    private void ComposeMeta(IContainer container)
    {
        container.Column(column =>
        {
            void AddText(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    column.Item().Text(Text(value));
            }

            column.Spacing(Mm(_style.HeaderLineGapMm));

            if (DocumentType == "cari_ledger")
            {
                AddText(CurrentCariLedgerDate());
                column.Item().Text(DefaultTitle);
                return;
            }

            AddText(_payload.Date);
            column.Item().Text("#" + Text(_payload.DocumentNo));
            column.Item().Text(FirstText(_payload.Title, DefaultTitle));
        });
    }

    private void ComposeBody(IContainer container)
    {
        switch (DocumentType)
        {
            case "quote":
                ComposeQuote(container);
                break;
            case "receipt":
                ComposeReceipt(container);
                break;
            case "cari_ledger":
                ComposeCariLedger(container);
                break;
            case "labeler":
                ComposeLabelerPage(container, ExpandedLabelerItems());
                break;
            default:
                ComposeItemsTable(container);
                break;
        }
    }

    private void ComposeLabelerDocument(IDocumentContainer container)
    {
        var labels = ExpandedLabelerItems();

        foreach (var pageLabels in labels.Chunk(LabelerLabelsPerPage))
        {
            container.Page(page =>
            {
                page.Size(210, 297, Unit.Millimetre);
                page.Margin(0);
                page.DefaultTextStyle(text => text.FontSize(6).FontColor(Colors.Black));
                page.Content().Element(element => ComposeLabelerPage(element, pageLabels));
            });
        }
    }

    private List<LabelerItem> ExpandedLabelerItems()
    {
        var labels = new List<LabelerItem>();

        if (_payload.Labels is null)
            return labels;

        foreach (var label in _payload.Labels)
        {
            for (var index = 0; index < label.LabelCount; index++)
                labels.Add(label);
        }

        return labels;
    }

    private void ComposeLabelerPage(IContainer container, IReadOnlyList<LabelerItem> labels)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var index = 0; index < LabelerColumns; index++)
                    columns.ConstantColumn(Mm(LabelerLabelWidthMm));
            });

            foreach (var label in labels)
            {
                table.Cell()
                    .Width(Mm(LabelerLabelWidthMm))
                    .Height(Mm(LabelerLabelHeightMm))
                    .Element(element => ComposeLabelerCell(element, label));
            }
        });
    }

    private void ComposeLabelerCell(IContainer container, LabelerItem label)
    {
        container
            .Background(Colors.White)
            .Padding(Mm(LabelerCellPaddingMm))
            .Row(row =>
            {
                row.ConstantItem(Mm(LabelerQrSizeMm))
                    .Height(Mm(LabelerQrSizeMm))
                    .Background(Colors.White)
                    .Svg(NormalizeSvgCode(label.SvgCode))
                    .FitArea();

                row.ConstantItem(Mm(1.5));

                row.RelativeItem()
                    .AlignMiddle()
                    .Text(Text(label.StockName))
                    .FontSize(6)
                    .Bold();
            });
    }

    private void ComposeCariLedger(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(Mm(6));
            column.Item().Element(ComposeCariLedgerTable);

            if (_payload.Metrics is { Count: > 0 })
                column.Item().AlignRight().Width(Mm(88)).Element(ComposeCariLedgerMetrics);
        });
    }

    private void ComposeCariLedgerSummary(IContainer container)
    {
        container.Column(column =>
        {
            void AddText(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    column.Item().Text(Text(value));
            }

            column.Spacing(Mm(2.2));
            column.Item().Text(FirstText(_payload.Title, DefaultTitle)).FontSize((float)(_style.BodyFontPx + 3)).Bold();
            column.Item().Text(Text(_payload.Cari?.Name)).FontSize((float)(_style.BodyFontPx + 1.4)).Bold();

            AddText(JoinNonEmpty(" / ", _payload.Cari?.Code, _payload.Cari?.ShortTitle, _payload.Cari?.Type));
        });
    }

    private void ComposeCariLedgerMetrics(IContainer container)
    {
        var metrics = _payload.Metrics?
            .Where(row => !string.IsNullOrWhiteSpace(row.Label) || !string.IsNullOrWhiteSpace(row.Value))
            .ToList() ?? [];

        if (metrics.Count == 0)
            return;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(Mm(34));
            });

            foreach (var metric in metrics)
            {
                table.Cell().Element(TotalChrome).Text(Text(metric.Label)).SemiBold();
                table.Cell().Element(TotalChrome).AlignRight().Text(CariLedgerMetricValue(metric));
            }
        });
    }

    private void ComposeCariLedgerTable(IContainer container)
    {
        var columns = _payload.Columns?
            .Where(column => !string.IsNullOrWhiteSpace(column.Key))
            .ToList() ?? [];

        container.Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                foreach (var column in columns)
                    definition.RelativeColumn((float)CariLedgerColumnWidth(column));
            });

            table.Header(header =>
            {
                foreach (var column in columns)
                {
                    header.Cell()
                        .Element(HeaderChrome)
                        .AlignCenter()
                        .Text(FirstText(column.Label, column.Key))
                        .FontSize((float)_style.TableFontPx);
                }
            });

            if (_payload.Rows is { Count: > 0 })
            {
                foreach (var row in _payload.Rows)
                {
                    foreach (var column in columns)
                    {
                        var text = row.Values.TryGetValue(Text(column.Key), out var value) ? CariLedgerCellText(value, column) : "";
                        var cell = table.Cell().Element(CellChrome);

                        cell = Text(column.Align).ToLowerInvariant() switch
                        {
                            "center" or "centre" => cell.AlignCenter(),
                            "right" => cell.AlignRight(),
                            _ => cell
                        };

                        cell.Text(text).FontSize((float)_style.TableFontPx);
                    }
                }
            }
            else
            {
                table.Cell()
                    .ColumnSpan((uint)columns.Count)
                    .Element(CellChrome)
                    .AlignCenter()
                    .Text("Cari hareketi yok.");
            }
        });
    }

    private double CariLedgerColumnWidth(CariLedgerColumn column)
    {
        var width = Clamp(column.Width, 1, 0.25, 100);

        return Text(column.Key).Equals("balance", StringComparison.OrdinalIgnoreCase)
            ? width * 1.35
            : width;
    }

    private string CariLedgerCellText(JsonElement value, CariLedgerColumn column)
    {
        var text = CellText(value);

        if (IsCariLedgerMoneyColumn(column))
            return FormatCariLedgerMoney(text);

        return text;
    }

    private void ComposeItemsTable(IContainer container)
    {
        if (_payload.Table is { Columns.Count: > 0 } customTable)
        {
            ComposeCustomTable(container, customTable);
            return;
        }

        ComposeLegacyItemsTable(container);
    }

    private void ComposeQuote(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(Mm(6));
            column.Item().Element(ComposeQuoteItemsTable);

            var hasDetails = _payload.DetailFields is { Count: > 0 };
            var hasTotals = HasVisibleLabeledRows(_payload.TotalRows);

            if (hasDetails || hasTotals)
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem(1.15f).Element(ComposeDetailFields);
                    row.RelativeItem(0.85f).Element(ComposeTotalRows);
                });
            }

            if (_payload.Signature is not null)
                column.Item().PaddingTop(Mm(10)).Element(ComposeSignature);
        });
    }

    private void ComposeQuoteItemsTable(IContainer container)
    {
        if (_payload.Items is not { Count: > 0 })
        {
            if (_payload.Table is { Columns.Count: > 0 } customTable)
            {
                ComposeCustomTable(container, customTable);
                return;
            }

            ComposeLegacyItemsTable(container);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(13);
                columns.RelativeColumn(35);
                columns.RelativeColumn(8);
                columns.RelativeColumn(8);
                columns.RelativeColumn(12);
                columns.RelativeColumn(7);
                columns.RelativeColumn(13);
            });

            table.Header(header =>
            {
                void HeaderCell(string text)
                {
                    header.Cell()
                        .Element(HeaderChrome)
                        .AlignCenter()
                        .Text(text)
                        .FontSize((float)_style.TableFontPx);
                }

                HeaderCell("Stok Kodu");
                HeaderCell("Ürün/Hizmet");
                HeaderCell("Miktar");
                HeaderCell("Birim");
                HeaderCell("Birim Fiyat");
                HeaderCell("KDV");
                HeaderCell("Tutar");
            });

            foreach (var item in _payload.Items)
            {
                BodyCell(item.Code);
                BodyCell(JoinNonEmpty("\n", item.Name, item.Description));
                BodyCell(item.Quantity, true);
                BodyCell(item.Unit, true);
                BodyCell(FirstText(item.UnitPrice, item.Price), true);
                BodyCell(FirstText(item.VatRate, item.Kdv, item.TaxRate), true);
                BodyCell(FirstText(item.Total, item.Amount, item.LineTotal), true);
            }

            void BodyCell(string? text, bool center = false)
            {
                var cell = table.Cell().Element(CellChrome);

                if (center)
                    cell = cell.AlignCenter();

                cell.Text(Text(text)).FontSize((float)_style.TableFontPx);
            }
        });
    }

    private void ComposeReceipt(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(Mm(6));
            column.Item().Element(ComposePaymentsTable);

            if (HasVisibleLabeledRows(_payload.PaymentTotals))
                column.Item().AlignRight().Width(Mm(78)).Element(element => ComposeLabeledRows(element, _payload.PaymentTotals));
        });
    }

    private void ComposePaymentsTable(IContainer container)
    {
        if (_payload.Payments is not { Count: > 0 })
        {
            if (_payload.Table is { Columns.Count: > 0 } customTable)
            {
                ComposeCustomTable(container, customTable);
                return;
            }

            container.Table(table =>
            {
                table.ColumnsDefinition(columns => columns.RelativeColumn());
                table.Cell().Element(CellChrome).AlignCenter().Text("Ödeme bulunmuyor.");
            });
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(18);
                columns.RelativeColumn(18);
                columns.RelativeColumn(19);
                columns.RelativeColumn(14);
                columns.RelativeColumn(13);
                columns.RelativeColumn(18);
            });

            table.Header(header =>
            {
                void HeaderCell(string text)
                {
                    header.Cell()
                        .Element(HeaderChrome)
                        .AlignCenter()
                        .Text(text)
                        .FontSize((float)_style.TableFontPx);
                }

                HeaderCell("Ödeme Tipi");
                HeaderCell("Hesap");
                HeaderCell("Banka/Şube");
                HeaderCell("Vade");
                HeaderCell("Çek No");
                HeaderCell("Tutar");
            });

            foreach (var payment in _payload.Payments)
            {
                BodyCell(payment.PaymentType);
                BodyCell(payment.Account);
                BodyCell(payment.BankBranch);
                BodyCell(payment.DueDate, true);
                BodyCell(payment.CheckNo, true);
                BodyCell(payment.Amount, true);
            }

            void BodyCell(string? text, bool center = false)
            {
                var cell = table.Cell().Element(CellChrome);

                if (center)
                    cell = cell.AlignCenter();

                cell.Text(Text(text)).FontSize((float)_style.TableFontPx);
            }
        });
    }

    private void ComposeDetailFields(IContainer container)
    {
        if (_payload.DetailFields is not { Count: > 0 })
            return;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(Mm(28));
                columns.RelativeColumn();
            });

            foreach (var field in _payload.DetailFields)
            {
                table.Cell().PaddingVertical(Mm(1.2)).Text(Text(field.Label)).SemiBold();
                table.Cell().PaddingVertical(Mm(1.2)).Text(Text(field.Value));
            }
        });
    }

    private void ComposeTotalRows(IContainer container)
    {
        if (!HasVisibleLabeledRows(_payload.TotalRows))
            return;

        ComposeLabeledRows(container, _payload.TotalRows);
    }

    private void ComposeLabeledRows(IContainer container, IReadOnlyList<LabeledValueRow>? rows)
    {
        var visibleRows = VisibleLabeledRows(rows);

        if (visibleRows.Count == 0)
            return;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(Mm(30));
            });

            foreach (var row in visibleRows)
            {
                Func<IContainer, IContainer> chrome = row.Emphasis ? TotalEmphasisChrome : TotalChrome;
                var labelCell = table.Cell().Element(chrome);
                var valueCell = table.Cell().Element(chrome).AlignRight();

                var label = labelCell.Text(Text(row.Label));
                var value = valueCell.Text(Text(row.Value));

                if (row.Emphasis)
                {
                    label.Bold();
                    value.Bold();
                }
            }
        });
    }

    private static bool HasVisibleLabeledRows(IReadOnlyList<LabeledValueRow>? rows)
    {
        return VisibleLabeledRows(rows).Count > 0;
    }

    private static List<LabeledValueRow> VisibleLabeledRows(IReadOnlyList<LabeledValueRow>? rows)
    {
        if (rows is null || rows.Count == 0)
            return [];

        return rows.Where(row => !row.HideIfZero || !IsZeroLike(row.Value)).ToList();
    }

    private static bool IsZeroLike(string? value)
    {
        var text = Text(value)
            .Replace("TL", "", StringComparison.OrdinalIgnoreCase)
            .Replace("TRY", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₺", "", StringComparison.OrdinalIgnoreCase)
            .Replace("%", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (text.Length == 0)
            return false;

        var normalized = text.Replace(" ", "");

        if (normalized.Contains(','))
            normalized = normalized.Replace(".", "").Replace(',', '.');

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return number == 0;

        return false;
    }

    private void ComposeSignature(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem()
                .MinHeight(Mm(24))
                .BorderTop(Mm(0.2))
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingTop(Mm(3))
                .Text(Text(_payload.Signature?.LeftText));

            row.ConstantItem(Mm(16));

            row.RelativeItem()
                .MinHeight(Mm(24))
                .BorderTop(Mm(0.2))
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingTop(Mm(3))
                .AlignCenter()
                .Text(Text(_payload.Signature?.RightText));
        });
    }

    private void ComposeCustomTable(IContainer container, DocumentTable customTable)
    {
        var columns = customTable.Columns
            .Where(column => !string.IsNullOrWhiteSpace(column.Key))
            .ToList();

        var rows = customTable.Rows is { Count: > 0 }
            ? customTable.Rows.Select(row => row.Values).ToList()
            : LegacyItemRows();

        container.Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                foreach (var column in columns)
                    definition.RelativeColumn((float)Clamp(column.Width, 1, 0.25, 100));
            });

            table.Header(header =>
            {
                foreach (var column in columns)
                {
                    header.Cell()
                        .Element(HeaderChrome)
                        .AlignCenter()
                        .Text(FirstText(column.Title, column.Key))
                        .FontSize((float)_style.TableFontPx);
                }
            });

            if (rows.Count > 0)
            {
                foreach (var row in rows)
                {
                    foreach (var column in columns)
                    {
                        var text = row.TryGetValue(Text(column.Key), out var value) ? CellText(value) : "";
                        var cell = table.Cell().Element(CellChrome);

                        cell = Text(column.Align).ToLowerInvariant() switch
                        {
                            "center" or "centre" => cell.AlignCenter(),
                            "right" => cell.AlignRight(),
                            _ => cell
                        };

                        cell.Text(text).FontSize((float)_style.TableFontPx);
                    }
                }
            }
            else
            {
                table.Cell()
                    .ColumnSpan((uint)columns.Count)
                    .Element(CellChrome)
                    .AlignCenter()
                    .Text("Kalem bulunmuyor.");
            }
        });
    }

    private List<Dictionary<string, JsonElement>> LegacyItemRows()
    {
        if (_payload.Items is not { Count: > 0 })
            return [];

        return _payload.Items.Select(item =>
        {
            var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            Add("code", item.Code);
            Add("name", item.Name);
            Add("description", item.Description);
            Add("name_description", JoinNonEmpty("\n", item.Name, item.Description));
            Add("quantity", item.Quantity);
            Add("unit", item.Unit);
            Add("unit_price", FirstText(item.UnitPrice, item.Price));
            Add("price", FirstText(item.Price, item.UnitPrice));
            Add("amount", FirstText(item.Amount, item.Total, item.LineTotal));
            Add("line_total", FirstText(item.LineTotal, item.Total, item.Amount));
            Add("total", FirstText(item.Total, item.Amount, item.LineTotal));
            Add("vat_rate", FirstText(item.VatRate, item.Kdv, item.TaxRate));
            Add("kdv", FirstText(item.Kdv, item.VatRate, item.TaxRate));
            Add("tax_rate", FirstText(item.TaxRate, item.Kdv, item.VatRate));
            Add("note", item.Note);
            Add("explanation", FirstText(item.Explanation, item.Aciklama, item.Note));
            Add("aciklama", FirstText(item.Aciklama, item.Explanation, item.Note));

            return values;

            void Add(string key, string? value)
            {
                values[key] = JsonSerializer.SerializeToElement(Text(value));
            }
        }).ToList();
    }

    private static string CellText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => Text(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };
    }

    private void ComposeLegacyItemsTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(17);
                columns.RelativeColumn(36);
                columns.RelativeColumn(8);
                columns.RelativeColumn(10);
                columns.RelativeColumn(7);
                columns.RelativeColumn(22);
            });

            table.Header(header =>
            {
                void HeaderCell(string text)
                {
                    header.Cell()
                        .Element(HeaderChrome)
                        .AlignCenter()
                        .Text(text)
                        .FontSize((float)_style.TableFontPx);
                }

                HeaderCell("Stok Kodu");
                HeaderCell("Stok İsmi");
                HeaderCell("Miktar");
                HeaderCell("Birim");
                HeaderCell("KDV");
                HeaderCell("Açıklama");
            });

            void BodyCell(string? text, bool center = false)
            {
                var cell = table.Cell().Element(CellChrome);

                if (center)
                    cell = cell.AlignCenter();

                cell.Text(Text(text)).FontSize((float)_style.TableFontPx);
            }

            if (_payload.Items is { Count: > 0 })
            {
                foreach (var item in _payload.Items)
                {
                    BodyCell(item.Code);
                    BodyCell(JoinNonEmpty("\n", item.Name, item.Description));
                    BodyCell(item.Quantity, true);
                    BodyCell(item.Unit, true);
                    BodyCell(FirstText(item.VatRate, item.Kdv, item.TaxRate), true);
                    BodyCell(FirstText(item.Explanation, item.Aciklama, item.Note));
                }
            }
            else
            {
                table.Cell()
                    .ColumnSpan(6)
                    .Element(CellChrome)
                    .AlignCenter()
                    .Text("Kalem bulunmuyor.");
            }
        });
    }

    private IContainer HeaderChrome(IContainer container)
    {
        return container
            .Border(Mm(_style.TableBorderWidthMm))
            .BorderColor(Colors.Grey.Lighten2)
            .MinHeight(Mm(_style.TableHeaderMinHeightMm))
            .PaddingVertical(Mm(_style.TableCellPaddingYMm))
            .PaddingHorizontal(Mm(_style.TableCellPaddingXMm));
    }

    private IContainer CellChrome(IContainer container)
    {
        return container
            .Border(Mm(_style.TableBorderWidthMm))
            .BorderColor(Colors.Grey.Lighten2)
            .MinHeight(Mm(_style.TableRowMinHeightMm))
            .PaddingVertical(Mm(_style.TableCellPaddingYMm))
            .PaddingHorizontal(Mm(_style.TableCellPaddingXMm));
    }

    private IContainer TotalChrome(IContainer container)
    {
        return container
            .BorderBottom(Mm(0.2))
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(Mm(1.6))
            .PaddingHorizontal(Mm(1.4));
    }

    private IContainer TotalEmphasisChrome(IContainer container)
    {
        return container
            .BorderBottom(Mm(0.35))
            .BorderColor(Colors.Grey.Darken1)
            .PaddingVertical(Mm(1.9))
            .PaddingHorizontal(Mm(1.4));
    }

    private static string FirstText(params string?[] values)
    {
        foreach (var value in values)
        {
            var text = Text(value);

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return "";
    }

    private static bool HasPartyContent(Party? party)
    {
        if (party is null)
            return false;

        return !string.IsNullOrWhiteSpace(FirstText(
            party.Name,
            party.Address,
            party.Phone,
            party.Email,
            party.TaxOffice,
            party.TaxNo,
            party.Tckn,
            party.DocumentType,
            party.DeliveryAddress));
    }

    private static string JoinNonEmpty(string separator, params string?[] values)
    {
        return string.Join(separator, values.Select(Text).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool IsCariLedgerMoneyColumn(CariLedgerColumn column)
    {
        var key = Text(column.Key).ToLowerInvariant();
        var label = Text(column.Label).ToLowerInvariant();

        return key is "debt" or "credit" or "balance" or "amount" or "total"
            || label.Contains("borc", StringComparison.Ordinal)
            || label.Contains("borç", StringComparison.Ordinal)
            || label.Contains("alacak", StringComparison.Ordinal)
            || label.Contains("bakiye", StringComparison.Ordinal)
            || label.Contains("tutar", StringComparison.Ordinal);
    }

    private static string CariLedgerMetricValue(LabeledValueRow metric)
    {
        var label = Text(metric.Label).ToLowerInvariant();
        var value = Text(metric.Value);

        if (value.Contains("TL", StringComparison.OrdinalIgnoreCase)
            || label.Contains("bakiye", StringComparison.Ordinal)
            || label.Contains("borc", StringComparison.Ordinal)
            || label.Contains("borç", StringComparison.Ordinal)
            || label.Contains("alacak", StringComparison.Ordinal)
            || label.Contains("net", StringComparison.Ordinal))
        {
            return FormatCariLedgerMoney(value);
        }

        return value;
    }

    private static string FormatCariLedgerMoney(string? value)
    {
        var text = Text(value);

        if (text.Length == 0)
            return "";

        var hasTl = text.Contains("TL", StringComparison.OrdinalIgnoreCase);
        var numericText = text
            .Replace("TL", "", StringComparison.OrdinalIgnoreCase)
            .Replace("TRY", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₺", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (!TryParseFlexibleDecimal(numericText, out var number))
            return text;

        var formatted = number.ToString("#,##0.00", new CultureInfo("tr-TR"));

        return hasTl ? formatted + " TL" : formatted;
    }

    private static bool TryParseFlexibleDecimal(string value, out decimal number)
    {
        number = 0;
        var text = Text(value);

        if (text.Length == 0)
            return false;

        var commaIndex = text.LastIndexOf(',');
        var dotIndex = text.LastIndexOf('.');

        if (commaIndex >= 0)
        {
            text = text.Replace(".", "").Replace(',', '.');
        }
        else if (dotIndex >= 0 && text.Length - dotIndex - 1 == 3)
        {
            text = text.Replace(".", "");
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
    }

    private string CurrentCariLedgerDate()
    {
        var text = FirstText(_payload.Date, _payload.ReportDate, _payload.GeneratedAt);

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(text))
            return text;

        return DateTimeOffset.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }
}

sealed class CatalogPriceListPdfDocument : IDocument
{
    private readonly PrintDocumentPayload _payload;
    private readonly byte[]? _logoBytes;
    private readonly IReadOnlyDictionary<string, byte[]> _productImages;
    private readonly NormalizedCatalogStyle _style;
    private readonly CatalogPalette _palette;

    public CatalogPriceListPdfDocument(
        PrintDocumentPayload payload,
        byte[]? logoBytes,
        IReadOnlyDictionary<string, byte[]> productImages)
    {
        _payload = payload;
        _logoBytes = logoBytes;
        _productImages = productImages;
        _style = NormalizedCatalogStyle.From(payload.CatalogStyle, payload.CatalogSummary);
        _palette = CatalogPalette.From(payload.Company?.Colors, payload.CatalogStyle);
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        foreach (var catalogPage in _payload.CatalogPages ?? [])
        {
            var generatedImageBytes = DecodeCatalogDataUri(catalogPage.GeneratedImage?.DataUri);

            if (generatedImageBytes is not null)
            {
                ComposeGeneratedImagePage(container, generatedImageBytes);
                continue;
            }

            container.Page(page =>
            {
                page.Size((float)_style.PageWidthMm, (float)_style.PageHeightMm, Unit.Millimetre);
                page.Margin(Mm(_style.PageMarginMm));
                page.DefaultTextStyle(text => text.FontSize(7).FontColor(Colors.Grey.Darken4));

                if (IsCover(catalogPage))
                {
                    page.Content().Element(element => ComposeCover(element, catalogPage));
                    page.Footer().Element(element => ComposeFooter(element, catalogPage));
                    return;
                }

                page.Header().Element(element => ComposeHeader(element, catalogPage));
                page.Content().Element(element => ComposeProducts(element, catalogPage));
                page.Footer().Element(element => ComposeFooter(element, catalogPage));
            });
        }
    }

    private void ComposeGeneratedImagePage(IDocumentContainer container, byte[] imageBytes)
    {
        container.Page(page =>
        {
            page.Size((float)_style.PageWidthMm, (float)_style.PageHeightMm, Unit.Millimetre);
            page.Margin(0);
            page.Content().Image(imageBytes).FitArea();
        });
    }

    private void ComposeCover(IContainer container, CatalogPage catalogPage)
    {
        container
            .AlignMiddle()
            .Column(column =>
            {
                column.Spacing(Mm(8));

                if (_logoBytes is not null)
                {
                    column.Item()
                        .Width(Mm(48))
                        .Height(Mm(18))
                        .Image(_logoBytes)
                        .FitArea();
                }

                column.Item()
                    .Text(FirstNonEmpty(catalogPage.Title, _payload.Title, "Katalog Fiyat Listesi"))
                    .FontSize(24)
                    .Bold()
                    .FontColor(_palette.Primary);

                var companyName = Text(_payload.Company?.Name);
                if (companyName.Length > 0)
                {
                    column.Item()
                        .Text(companyName)
                        .FontSize(11)
                        .FontColor(_palette.Muted);
                }

                var contactInfo = Text(_payload.Company?.ContactInfo);
                if (contactInfo.Length > 0)
                {
                    column.Item()
                        .Text(contactInfo)
                        .FontSize(8)
                        .FontColor(_palette.Muted);
                }

                column.Item()
                    .PaddingTop(Mm(8))
                    .Element(ComposeCoverSummary);
            });
    }

    private void ComposeCoverSummary(IContainer container)
    {
        container
            .Border(Mm(0.3))
            .BorderColor(_palette.Border)
            .Background(_palette.LightBackground)
            .Padding(Mm(5))
            .Row(row =>
            {
                row.RelativeItem().Element(element => ComposeMetric(element, "Sayfa", _payload.CatalogSummary?.PageCount));
                row.RelativeItem().Element(element => ComposeMetric(element, "Ürün", _payload.CatalogSummary?.ProductCount));
                row.RelativeItem().Element(element => ComposeMetric(element, "Kategori", _payload.CatalogSummary?.CategoryCount));
                row.RelativeItem().Element(element => ComposeMetric(element, "Eksik fiyat", _payload.CatalogHealth?.MissingPriceCount));
            });
    }

    private void ComposeMetric(IContainer container, string label, int? value)
    {
        container.Column(column =>
        {
            column.Spacing(Mm(1));
            column.Item().Text(label).FontSize(6).FontColor(_palette.Muted);
            column.Item().Text(Math.Max(0, value ?? 0).ToString(CultureInfo.InvariantCulture)).FontSize(12).Bold().FontColor(_palette.Primary);
        });
    }

    private void ComposeHeader(IContainer container, CatalogPage catalogPage)
    {
        container
            .PaddingBottom(Mm(5))
            .BorderBottom(Mm(0.35))
            .BorderColor(_palette.Border)
            .Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Spacing(Mm(1.2));
                    column.Item()
                        .Text(FirstNonEmpty(catalogPage.Title, catalogPage.Category, _payload.Title, "Katalog Fiyat Listesi"))
                        .FontSize(13)
                        .Bold()
                        .FontColor(_palette.Primary);

                    var subtitle = FirstNonEmpty(catalogPage.Category, _payload.Company?.Name);
                    if (subtitle.Length > 0)
                        column.Item().Text(subtitle).FontSize(7).FontColor(_palette.Muted);
                });

                row.ConstantItem(Mm(38)).AlignRight().Column(column =>
                {
                    column.Spacing(Mm(1));
                    column.Item().Text("Sayfa").FontSize(6).FontColor(_palette.Muted);
                    column.Item().Text(PageNumberText(catalogPage)).FontSize(10).Bold().FontColor(_palette.Primary);
                });
            });
    }

    private void ComposeProducts(IContainer container, CatalogPage catalogPage)
    {
        var products = CatalogProducts(catalogPage);

        container.PaddingTop(Mm(5)).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var index = 0; index < _style.Columns; index++)
                    columns.RelativeColumn();
            });

            foreach (var product in products)
            {
                table.Cell()
                    .Padding(Mm(1))
                    .Element(element => ComposeProductCard(element, product));
            }
        });
    }

    private void ComposeProductCard(IContainer container, CatalogProduct product)
    {
        var imageBytes = ProductImage(product);
        var showImageSlot = _style.ShowProductImages
            && (imageBytes is not null || _style.ShowMissingImagePlaceholder);

        container
            .MinHeight(Mm(_style.ProductCardMinHeightMm))
            .Border(Mm(0.3))
            .BorderColor(_palette.Border)
            .Background(Colors.White)
            .Padding(Mm(2.2))
            .Row(row =>
            {
                if (showImageSlot)
                {
                    row.ConstantItem(Mm(_style.ProductImageSizeMm))
                        .Height(Mm(_style.ProductImageSizeMm))
                        .Element(element => ComposeProductImage(element, product, imageBytes));
                }

                row.RelativeItem()
                    .PaddingLeft(showImageSlot ? Mm(2.2) : 0)
                    .Column(column =>
                    {
                        column.Spacing(Mm(0.9));

                        var code = FirstNonEmpty(product.CatalogCode, product.Sku, product.Id);
                        if (code.Length > 0)
                            column.Item().Text(Truncate(code, 44)).FontSize(5.7f).FontColor(_palette.Muted);

                        column.Item()
                            .Text(Truncate(FirstNonEmpty(product.Name, "Ürün"), _style.NameMaxLength))
                            .FontSize(_style.NameFontSize)
                            .Bold()
                            .FontColor(_palette.Primary);

                        var detail = FirstNonEmpty(product.Brand, product.Category, product.Description);
                        if (detail.Length > 0)
                            column.Item().Text(Truncate(detail, _style.DetailMaxLength)).FontSize(5.8f).FontColor(_palette.Muted);

                        column.Item().Row(metaRow =>
                        {
                            metaRow.RelativeItem().Column(meta =>
                            {
                                meta.Spacing(Mm(0.6));
                                var vat = FirstNonEmpty(product.VatLabel, product.Stock);
                                if (vat.Length > 0)
                                    meta.Item().Text(Truncate(vat, 36)).FontSize(5.4f).FontColor(_palette.Muted);
                            });

                            metaRow.ConstantItem(Mm(_style.PriceColumnWidthMm))
                                .AlignRight()
                                .AlignBottom()
                                .Text(FirstNonEmpty(product.PriceDisplay, "-"))
                                .FontSize(_style.PriceFontSize)
                                .Bold()
                                .FontColor(_palette.Price);
                        });
                    });
            });
    }

    private void ComposeProductImage(IContainer container, CatalogProduct product, byte[]? imageBytes)
    {
        container
            .Border(Mm(0.25))
            .BorderColor(_palette.Border)
            .Background(_palette.ImageBackground)
            .Padding(Mm(1))
            .Element(element =>
            {
                if (imageBytes is not null)
                {
                    element.Image(imageBytes).FitArea();
                    return;
                }

                element.AlignCenter()
                    .AlignMiddle()
                    .Text(ProductInitials(product))
                    .FontSize(7)
                    .SemiBold()
                    .FontColor(_palette.Muted);
            });
    }

    private void ComposeFooter(IContainer container, CatalogPage catalogPage)
    {
        container
            .PaddingTop(Mm(3.5))
            .BorderTop(Mm(0.25))
            .BorderColor(_palette.Border)
            .Row(row =>
            {
                row.RelativeItem()
                    .Text(FirstNonEmpty(_payload.Company?.Name, _payload.Title, "Katalog"))
                    .FontSize(6)
                    .FontColor(_palette.Muted);

                row.ConstantItem(Mm(46))
                    .AlignRight()
                    .Text(GeneratedAtText())
                    .FontSize(6)
                    .FontColor(_palette.Muted);
            });
    }

    private static bool IsCover(CatalogPage page)
    {
        return Text(page.Type).Equals("cover", StringComparison.OrdinalIgnoreCase);
    }

    private static List<CatalogProduct> CatalogProducts(CatalogPage page)
    {
        return page.Products?
            .Where(HasProductContent)
            .ToList() ?? [];
    }

    private static bool HasProductContent(CatalogProduct product)
    {
        return FirstNonEmpty(product.Name, product.Sku, product.CatalogCode, product.PriceDisplay).Length > 0;
    }

    private string PageNumberText(CatalogPage page)
    {
        var total = _payload.CatalogSummary?.PageCount;

        if (total is > 0)
            return $"{Math.Max(1, page.PageNumberValue ?? 1)}/{total.Value}";

        return Math.Max(1, page.PageNumberValue ?? 1).ToString(CultureInfo.InvariantCulture);
    }

    private string GeneratedAtText()
    {
        var text = Text(_payload.GeneratedAt);

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

        return text.Length > 0 ? text : DateTimeOffset.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private byte[]? ProductImage(CatalogProduct product)
    {
        var imageUrl = Text(product.ImageUrl);
        return imageUrl.Length > 0 && _productImages.TryGetValue(imageUrl, out var imageBytes)
            ? imageBytes
            : null;
    }

    private static string ProductInitials(CatalogProduct product)
    {
        var source = FirstNonEmpty(product.Brand, product.Name, product.CatalogCode, product.Sku, "Ürün");
        var initials = string.Concat(source
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(word => word[0]))
            .ToUpperInvariant();

        return initials.Length > 0 ? initials : "Ü";
    }

    private static string Truncate(string? value, int maxLength)
    {
        var text = Text(value);

        if (text.Length <= maxLength)
            return text;

        return text[..Math.Max(0, maxLength - 1)] + "…";
    }
}

sealed class NormalizedCatalogStyle
{
    public double PageWidthMm { get; init; } = 210;
    public double PageHeightMm { get; init; } = 297;
    public double PageMarginMm { get; init; } = 10;
    public int Columns { get; init; } = 2;
    public double ProductCardMinHeightMm { get; init; } = 25.8;
    public bool ShowProductImages { get; init; } = true;
    public bool ShowMissingImagePlaceholder { get; init; } = false;
    public double ProductImageSizeMm { get; init; } = 18;
    public double PriceColumnWidthMm { get; init; } = 35;
    public float PriceFontSize { get; init; } = 8.1f;
    public float NameFontSize { get; init; } = 7.1f;
    public int NameMaxLength { get; init; } = 82;
    public int DetailMaxLength { get; init; } = 88;

    public static NormalizedCatalogStyle From(CatalogPrintStyle? style, CatalogSummary? summary)
    {
        var pageSize = Text(style?.PageSize).ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal);
        var isLandscape = pageSize.Contains("landscape", StringComparison.Ordinal);
        var isSquare = pageSize.Contains("square", StringComparison.Ordinal);
        var isStory = pageSize.Contains("story", StringComparison.Ordinal);
        var priceEmphasis = Text(style?.PriceEmphasis).ToLowerInvariant();
        var missingImageBehavior = Text(style?.MissingImageBehavior).ToLowerInvariant();
        var isProminentPrice = priceEmphasis is "prominent" or "strong" or "bold" or "vurgulu";
        var isSubtlePrice = priceEmphasis is "subtle" or "soft" or "sade";
        var itemsPerPage = summary?.ItemsPerPage is > 0 ? summary.ItemsPerPage.Value : 16;
        var columns = isStory || isSquare ? 1 : isLandscape ? 4 : 2;

        return new NormalizedCatalogStyle
        {
            PageWidthMm = isSquare ? 160 : isStory ? 108 : isLandscape ? 297 : 210,
            PageHeightMm = isSquare ? 160 : isStory ? 192 : isLandscape ? 210 : 297,
            PageMarginMm = isStory ? 6 : isSquare ? 8 : isLandscape ? 8 : 10,
            Columns = columns,
            ProductCardMinHeightMm = isStory
                ? 36
                : isSquare
                    ? 34
                    : isLandscape
                ? 29
                : itemsPerPage <= 12 ? 31 : 25.8,
            ShowProductImages = true,
            ProductImageSizeMm = isLandscape ? 14 : isStory ? 20 : isSquare ? 22 : 18,
            PriceColumnWidthMm = isLandscape ? 25 : isStory ? 30 : 35,
            PriceFontSize = isProminentPrice ? 9.4f : isSubtlePrice ? 7.4f : 8.1f,
            NameFontSize = isStory || isSquare ? 7.8f : 7.1f,
            NameMaxLength = isLandscape ? 54 : isStory ? 62 : 82,
            DetailMaxLength = isLandscape ? 48 : isStory ? 60 : 88,
            ShowMissingImagePlaceholder = missingImageBehavior is not "text_only" and not "text-only" and not "text"
        };
    }
}

sealed class CatalogPalette
{
    public string Primary { get; init; } = "#102A43";
    public string Accent { get; init; } = "#0B5CAD";
    public string Price { get; init; } = "#0B5CAD";
    public string Muted { get; init; } = "#52616F";
    public string Border { get; init; } = "#D8DEE8";
    public string LightBackground { get; init; } = "#F7F9FC";
    public string ImageBackground { get; init; } = "#F8FAFC";

    public static CatalogPalette From(IReadOnlyList<string>? brandColors, CatalogPrintStyle? style)
    {
        var colors = (brandColors ?? [])
            .Select(NormalizeHexColor)
            .Where(color => color.Length > 0)
            .Take(3)
            .ToList();

        var theme = Text(style?.Theme).ToLowerInvariant();
        var priceEmphasis = Text(style?.PriceEmphasis).ToLowerInvariant();
        var defaultPrimary = theme switch
        {
            "premium" => "#1F2937",
            "campaign" or "kampanya" => "#8A1C1C",
            "minimal" => "#111827",
            _ => "#102A43"
        };

        var primary = colors.ElementAtOrDefault(0) ?? defaultPrimary;
        var accent = colors.ElementAtOrDefault(1) ?? (theme is "campaign" or "kampanya" ? "#D97706" : "#0B5CAD");
        var muted = colors.ElementAtOrDefault(2) ?? "#52616F";
        var price = priceEmphasis is "prominent" or "strong" or "bold" or "vurgulu"
            ? accent
            : primary;

        return new CatalogPalette
        {
            Primary = primary,
            Accent = accent,
            Price = price,
            Muted = muted
        };
    }

    private static string NormalizeHexColor(string? value)
    {
        var text = Text(value).Trim();
        if (text.Length == 0)
            return "";

        if (!text.StartsWith('#'))
            text = "#" + text;

        var hex = text[1..];
        if (hex.Length == 3 && hex.All(Uri.IsHexDigit))
            return "#" + string.Concat(hex.Select(character => new string(character, 2))).ToUpperInvariant();

        return hex.Length == 6 && hex.All(Uri.IsHexDigit)
            ? "#" + hex.ToUpperInvariant()
            : "";
    }
}

static class PdfHelpers
{
    public static string SafeFileName(string? value, string fallback)
    {
        var text = Text(value);

        if (string.IsNullOrWhiteSpace(text))
            text = fallback;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            text = text.Replace(invalid, '-');

        return text;
    }

    public static string Text(string? value) => value?.Trim() ?? "";

    public static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var text = Text(value);

            if (text.Length > 0)
                return text;
        }

        return "";
    }

    public static string CatalogImageMimeFromDataUri(string? value)
    {
        var text = Text(value);

        if (!text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return "";

        var commaIndex = text.IndexOf(',');
        var semicolonIndex = text.IndexOf(';');
        var endIndex = semicolonIndex >= 0 && (commaIndex < 0 || semicolonIndex < commaIndex)
            ? semicolonIndex
            : commaIndex;

        return endIndex > 5 ? text[5..endIndex].Trim().ToLowerInvariant() : "";
    }

    public static string CatalogImageBase64FromDataUri(string? value)
    {
        var text = Text(value);
        var commaIndex = text.IndexOf(',');

        if (commaIndex < 0 || commaIndex >= text.Length - 1)
            return "";

        return text[(commaIndex + 1)..].Replace(" ", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal);
    }

    public static bool IsAllowedCatalogImageMime(string? value)
    {
        return Text(value).ToLowerInvariant() is "image/png" or "image/jpeg" or "image/jpg" or "image/webp";
    }

    public static int EstimateCatalogBase64ByteLength(string base64)
    {
        var text = Text(base64);

        if (text.Length == 0 || text.Length % 4 != 0)
            throw new FormatException("Invalid base64 length.");

        var padding = text.EndsWith("==", StringComparison.Ordinal) ? 2 : text.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;
        return Math.Max(0, (text.Length * 3 / 4) - padding);
    }

    public static byte[]? DecodeCatalogDataUri(string? value)
    {
        var text = Text(value);

        if (!IsAllowedCatalogImageMime(CatalogImageMimeFromDataUri(text)))
            return null;

        var base64 = CatalogImageBase64FromDataUri(text);

        if (base64.Length == 0)
            return null;

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string NormalizeSvgCode(string? value)
    {
        var text = WebUtility.HtmlDecode(Text(value))
            .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        if (!text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
            return text;

        var insertAt = text.IndexOf('>');

        if (insertAt < 0)
            return text;

        if (!text.Contains("shape-rendering=", StringComparison.OrdinalIgnoreCase))
            text = text.Insert(insertAt, " shape-rendering=\"crispEdges\"");

        insertAt = text.IndexOf('>');

        if (!text.Contains("preserveAspectRatio=", StringComparison.OrdinalIgnoreCase))
            text = text.Insert(insertAt, " preserveAspectRatio=\"xMidYMid meet\"");

        return text;
    }

    public static float Mm(double value) => (float)(value * 72 / 25.4);

    public static double Clamp(double? value, double fallback, double min, double max)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return fallback;

        return Math.Min(Math.Max(value.Value, min), max);
    }
}

sealed class NormalizedPrintStyle
{
    public double PageMarginMm { get; init; }
    public double LogoWidthMm { get; init; }
    public double LogoHeightMm { get; init; }
    public double BodyFontPx { get; init; }
    public double TableFontPx { get; init; }
    public double HeaderGapMm { get; init; }
    public double HeaderMetaWidthMm { get; init; }
    public double HeaderLineGapMm { get; init; }
    public double TableCellPaddingYMm { get; init; }
    public double TableCellPaddingXMm { get; init; }
    public double TableBorderWidthMm { get; init; }
    public double TableHeaderMinHeightMm { get; init; }
    public double TableRowMinHeightMm { get; init; }
    public double CustomerBlockPaddingLeftMm { get; init; }
    public double CustomerLabelWidthMm { get; init; }
    public float CompanyColumnWeight { get; init; }
    public float CustomerColumnWeight { get; init; }
    public PageSize PaperSize { get; init; } = PageSizes.A4;

    public static NormalizedPrintStyle From(PrintStyle? value)
    {
        var paperSizeName = PaperSizeNameFrom(value?.PaperSize);
        var isA5 = paperSizeName == "a5";

        return new NormalizedPrintStyle
        {
            PageMarginMm = Clamp(value?.PageMarginMm, isA5 ? 8 : 10, 3, 20),
            LogoWidthMm = Clamp(value?.LogoWidthMm, isA5 ? 38 : 42, 16, 70),
            LogoHeightMm = Clamp(value?.LogoHeightMm, isA5 ? 12 : 14, 6, 28),
            BodyFontPx = Clamp(value?.BodyFontPx, isA5 ? 6.8 : 7.2, 4.8, 11),
            TableFontPx = Clamp(value?.TableFontPx, isA5 ? 6.5 : 6.9, 4.8, 10),
            HeaderGapMm = Clamp(value?.HeaderGapMm, isA5 ? 22 : 24, 4, 30),
            HeaderMetaWidthMm = Clamp(value?.HeaderMetaWidthMm, isA5 ? 24 : 26, 14, 42),
            HeaderLineGapMm = isA5 ? 1.1 : 1.25,
            TableCellPaddingYMm = Clamp(value?.TableCellPaddingYMm, isA5 ? 1.7 : 1.9, 0.6, 5),
            TableCellPaddingXMm = Clamp(value?.TableCellPaddingXMm, isA5 ? 1.2 : 1.4, 0.5, 4),
            TableBorderWidthMm = 0.2,
            TableHeaderMinHeightMm = isA5 ? 6.8 : 7.0,
            TableRowMinHeightMm = isA5 ? 10.5 : 10.8,
            CustomerBlockPaddingLeftMm = isA5 ? 3.5 : 4.5,
            CustomerLabelWidthMm = isA5 ? 22 : 25,
            CompanyColumnWeight = isA5 ? 1.28f : 1.18f,
            CustomerColumnWeight = isA5 ? 1.0f : 1.04f,
            PaperSize = PaperSizeFrom(paperSizeName)
        };
    }

    private static PageSize PaperSizeFrom(string? value)
    {
        var normalized = PaperSizeNameFrom(value);

        return normalized switch
        {
            "a5" => PageSizes.A5.Landscape(),
            "a4" => PageSizes.A4,
            _ => PageSizes.A4
        };
    }

    private static string PaperSizeNameFrom(string? value)
    {
        var normalized = Text(value).ToLowerInvariant().Replace("-", "").Replace("_", "");
        return normalized == "a5" ? "a5" : "a4";
    }
}

sealed class PrintDocumentPayload
{
    [JsonPropertyName("document_type")]
    public string? DocumentType { get; init; }

    [JsonPropertyName("document_no")]
    public string? DocumentNo { get; init; }

    [JsonPropertyName("date")]
    public string? Date { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("print_style")]
    public PrintStyle? PrintStyle { get; init; }

    [JsonPropertyName("company")]
    public Party? Company { get; init; }

    [JsonPropertyName("customer")]
    public Party? Customer { get; init; }

    [JsonPropertyName("order")]
    public OrderInfo? Order { get; init; }

    [JsonPropertyName("table")]
    public DocumentTable? Table { get; init; }

    [JsonPropertyName("items")]
    public List<OrderItem>? Items { get; init; }

    [JsonPropertyName("labels")]
    public List<LabelerItem>? Labels { get; init; }

    [JsonPropertyName("detail_fields")]
    public List<LabeledValueRow>? DetailFields { get; init; }

    [JsonPropertyName("total_rows")]
    public List<LabeledValueRow>? TotalRows { get; init; }

    [JsonPropertyName("signature")]
    public SignatureBlock? Signature { get; init; }

    [JsonPropertyName("receipt_type")]
    public string? ReceiptType { get; init; }

    [JsonPropertyName("payments")]
    public List<PaymentRow>? Payments { get; init; }

    [JsonPropertyName("payment_totals")]
    public List<LabeledValueRow>? PaymentTotals { get; init; }

    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; init; }

    [JsonPropertyName("report_date")]
    public string? ReportDate { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("cari")]
    public CariLedgerParty? Cari { get; init; }

    [JsonPropertyName("view")]
    public CariLedgerView? View { get; init; }

    [JsonPropertyName("filters")]
    public CariLedgerFilters? Filters { get; init; }

    [JsonPropertyName("metrics")]
    public List<LabeledValueRow>? Metrics { get; init; }

    [JsonPropertyName("findings")]
    public List<string>? Findings { get; init; }

    [JsonPropertyName("columns")]
    public List<CariLedgerColumn>? Columns { get; init; }

    [JsonPropertyName("rows")]
    public List<DocumentTableRow>? Rows { get; init; }

    [JsonPropertyName("style")]
    public CatalogPrintStyle? CatalogStyle { get; init; }

    [JsonPropertyName("summary")]
    public CatalogSummary? CatalogSummary { get; init; }

    [JsonPropertyName("health")]
    public CatalogHealth? CatalogHealth { get; init; }

    [JsonPropertyName("pages")]
    public List<CatalogPage>? CatalogPages { get; init; }
}

sealed class CariLedgerParty
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("short_title")]
    public string? ShortTitle { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

sealed class LabelerItem
{
    [JsonPropertyName("svg_kod")]
    public string? SvgCode { get; init; }

    [JsonPropertyName("stok_adi")]
    public string? StockName { get; init; }

    [JsonPropertyName("etiket_adedi")]
    public int LabelCount { get; init; }
}

sealed class CatalogPrintStyle
{
    [JsonPropertyName("page_size")]
    public string? PageSize { get; init; }

    [JsonPropertyName("theme")]
    public string? Theme { get; init; }

    [JsonPropertyName("tone")]
    public string? Tone { get; init; }

    [JsonPropertyName("price_emphasis")]
    public string? PriceEmphasis { get; init; }

    [JsonPropertyName("missing_image_behavior")]
    public string? MissingImageBehavior { get; init; }
}

sealed class CatalogSummary
{
    [JsonPropertyName("page_count")]
    public int? PageCount { get; init; }

    [JsonPropertyName("product_count")]
    public int? ProductCount { get; init; }

    [JsonPropertyName("category_count")]
    public int? CategoryCount { get; init; }

    [JsonPropertyName("items_per_page")]
    public int? ItemsPerPage { get; init; }
}

sealed class CatalogHealth
{
    [JsonPropertyName("total_product_count")]
    public int? TotalProductCount { get; init; }

    [JsonPropertyName("eligible_product_count")]
    public int? EligibleProductCount { get; init; }

    [JsonPropertyName("missing_price_count")]
    public int? MissingPriceCount { get; init; }

    [JsonPropertyName("missing_image_count")]
    public int? MissingImageCount { get; init; }
}

sealed class CatalogPage
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("page_number")]
    public int? PageNumberSnake { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("products")]
    public List<CatalogProduct>? Products { get; init; }

    [JsonPropertyName("generated_image")]
    public CatalogGeneratedImage? GeneratedImage { get; init; }

    [JsonIgnore]
    public int? PageNumberValue => PageNumber ?? PageNumberSnake;
}

sealed class CatalogProduct
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("sku")]
    public string? Sku { get; init; }

    [JsonPropertyName("catalog_code")]
    public string? CatalogCode { get; init; }

    [JsonPropertyName("brand")]
    public string? Brand { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("price_display")]
    public string? PriceDisplay { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("vat_label")]
    public string? VatLabel { get; init; }

    [JsonPropertyName("stock")]
    public string? Stock { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }
}

sealed class CatalogGeneratedImage
{
    [JsonPropertyName("data_uri")]
    public string? DataUri { get; init; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; init; }

    [JsonPropertyName("byte_length")]
    public int? ByteLength { get; init; }
}

sealed class CariLedgerView
{
    [JsonPropertyName("movement_mode")]
    public string? MovementMode { get; init; }

    [JsonPropertyName("movement_limit")]
    public int? MovementLimit { get; init; }

    [JsonPropertyName("movement_cursor")]
    public int? MovementCursor { get; init; }

    [JsonPropertyName("density")]
    public string? Density { get; init; }

    [JsonPropertyName("movement_count")]
    public int? MovementCount { get; init; }

    [JsonPropertyName("page_count")]
    public int? PageCount { get; init; }

    [JsonPropertyName("page_remaining")]
    public int? PageRemaining { get; init; }
}

sealed class CariLedgerFilters
{
    [JsonPropertyName("date_from")]
    public string? DateFrom { get; init; }

    [JsonPropertyName("date_to")]
    public string? DateTo { get; init; }

    [JsonPropertyName("entry_kind")]
    public string? EntryKind { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }
}

sealed class CariLedgerColumn
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("align")]
    public string? Align { get; init; }

    [JsonPropertyName("width")]
    public double? Width { get; init; }
}

sealed class LabeledValueRow
{
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("emphasis")]
    public bool Emphasis { get; init; }

    [JsonPropertyName("hide_if_zero")]
    public bool HideIfZero { get; init; }
}

sealed class SignatureBlock
{
    [JsonPropertyName("left_text")]
    public string? LeftText { get; init; }

    [JsonPropertyName("right_text")]
    public string? RightText { get; init; }
}

sealed class PaymentRow
{
    [JsonPropertyName("payment_type")]
    public string? PaymentType { get; init; }

    [JsonPropertyName("account")]
    public string? Account { get; init; }

    [JsonPropertyName("bank_branch")]
    public string? BankBranch { get; init; }

    [JsonPropertyName("due_date")]
    public string? DueDate { get; init; }

    [JsonPropertyName("check_no")]
    public string? CheckNo { get; init; }

    [JsonPropertyName("amount")]
    public string? Amount { get; init; }
}

sealed class DocumentTable
{
    [JsonPropertyName("columns")]
    public List<DocumentTableColumn> Columns { get; init; } = [];

    [JsonPropertyName("rows")]
    public List<DocumentTableRow>? Rows { get; init; }
}

sealed class DocumentTableColumn
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("width")]
    public double? Width { get; init; }

    [JsonPropertyName("align")]
    public string? Align { get; init; }
}

sealed class DocumentTableRow
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

sealed class PrintStyle
{
    [JsonPropertyName("page_margin_mm")]
    public double? PageMarginMm { get; init; }

    [JsonPropertyName("logo_width_mm")]
    public double? LogoWidthMm { get; init; }

    [JsonPropertyName("logo_height_mm")]
    public double? LogoHeightMm { get; init; }

    [JsonPropertyName("body_font_px")]
    public double? BodyFontPx { get; init; }

    [JsonPropertyName("table_font_px")]
    public double? TableFontPx { get; init; }

    [JsonPropertyName("header_gap_mm")]
    public double? HeaderGapMm { get; init; }

    [JsonPropertyName("header_meta_width_mm")]
    public double? HeaderMetaWidthMm { get; init; }

    [JsonPropertyName("table_cell_padding_y_mm")]
    public double? TableCellPaddingYMm { get; init; }

    [JsonPropertyName("table_cell_padding_x_mm")]
    public double? TableCellPaddingXMm { get; init; }

    [JsonPropertyName("paper_size")]
    public string? PaperSize { get; init; }
}

sealed class Party
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("tax_office")]
    public string? TaxOffice { get; init; }

    [JsonPropertyName("tax_no")]
    public string? TaxNo { get; init; }

    [JsonPropertyName("tckn")]
    public string? Tckn { get; init; }

    [JsonPropertyName("document_type")]
    public string? DocumentType { get; init; }

    [JsonPropertyName("delivery_address")]
    public string? DeliveryAddress { get; init; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("contact_info")]
    public string? ContactInfo { get; init; }

    [JsonPropertyName("colors")]
    public List<string>? Colors { get; init; }
}

sealed class OrderInfo
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("delivery_address")]
    public string? DeliveryAddress { get; init; }

    [JsonPropertyName("shipping_method")]
    public string? ShippingMethod { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

sealed class OrderItem
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("quantity")]
    public string? Quantity { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("unit_price")]
    public string? UnitPrice { get; init; }

    [JsonPropertyName("price")]
    public string? Price { get; init; }

    [JsonPropertyName("amount")]
    public string? Amount { get; init; }

    [JsonPropertyName("line_total")]
    public string? LineTotal { get; init; }

    [JsonPropertyName("total")]
    public string? Total { get; init; }

    [JsonPropertyName("vat_rate")]
    public string? VatRate { get; init; }

    [JsonPropertyName("kdv")]
    public string? Kdv { get; init; }

    [JsonPropertyName("tax_rate")]
    public string? TaxRate { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; init; }

    [JsonPropertyName("aciklama")]
    public string? Aciklama { get; init; }
}
