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

app.MapPost("/render/order-slip", async Task<IResult> (PrintDocumentPayload payload, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    var authError = RequireApiKey(httpContext, apiKey);
    if (authError is not null)
        return authError;

    var error = Validate(payload);

    if (!string.IsNullOrWhiteSpace(error))
        return Results.BadRequest(error);

    var logoBytes = await TryFetchLogoAsync(payload.Company?.LogoUrl, httpClientFactory);
    var pdf = new PrintDocumentPdfDocument(payload, logoBytes).GeneratePdf();
    var fileName = SafeFileName(payload.DocumentNo, "order-slip") + ".pdf";

    return Results.File(pdf, "application/pdf", fileName);
});

app.MapPost("/render/order-slip-url", async Task<IResult> (PrintDocumentPayload payload, IHttpClientFactory httpClientFactory, HttpContext httpContext) =>
{
    var authError = RequireApiKey(httpContext, apiKey);
    if (authError is not null)
        return authError;

    var error = Validate(payload);

    if (!string.IsNullOrWhiteSpace(error))
        return Results.BadRequest(new
        {
            ok = false,
            error
        });

    var logoBytes = await TryFetchLogoAsync(payload.Company?.LogoUrl, httpClientFactory);
    var pdf = new PrintDocumentPdfDocument(payload, logoBytes).GeneratePdf();
    var fileName = UniquePdfFileName(payload.DocumentNo, "order-slip");
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

app.Run();

static IResult? RequireApiKey(HttpContext httpContext, string? configuredApiKey)
{
    if (string.IsNullOrWhiteSpace(configuredApiKey))
        return Results.Problem("QUESTPDF_API_KEY is not configured.", statusCode: StatusCodes.Status500InternalServerError);

    var requestApiKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();

    if (!string.Equals(requestApiKey, configuredApiKey, StringComparison.Ordinal))
        return Results.Unauthorized();

    return null;
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
        return "document_type 'quote', 'receipt' veya 'order_slip' olmalidir.";

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
    return documentType is "quote" or "receipt" or "order_slip";
}

static async Task<byte[]?> TryFetchLogoAsync(string? logoUrl, IHttpClientFactory httpClientFactory)
{
    if (!Uri.TryCreate(Text(logoUrl), UriKind.Absolute, out var uri))
        return null;

    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        return null;

    try
    {
        var client = httpClientFactory.CreateClient("logo");
        return await client.GetByteArrayAsync(uri);
    }
    catch
    {
        return null;
    }
}

static string UniquePdfFileName(string? documentNo, string fallback)
{
    var baseName = SafeFileName(documentNo, fallback);
    return baseName + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N")[..8] + ".pdf";
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
    private readonly PrintDocumentPayload _payload;
    private readonly byte[]? _logoBytes;
    private readonly NormalizedPrintStyle _style;

    public PrintDocumentPdfDocument(PrintDocumentPayload payload, byte[]? logoBytes)
    {
        _payload = payload;
        _logoBytes = logoBytes;
        _style = NormalizedPrintStyle.From(payload.PrintStyle);
    }

    private string DocumentType => Text(_payload.DocumentType);

    private bool HasCustomer => HasPartyContent(_payload.Customer);

    private string DefaultTitle => DocumentType switch
    {
        "quote" => "Teklif",
        "receipt" => "Makbuz",
        _ => "Sipariş Fişi"
    };

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(_style.PaperSize);
            page.Margin(Mm(_style.PageMarginMm));
            page.DefaultTextStyle(text => text.FontSize((float)_style.BodyFontPx).FontColor(Colors.Grey.Darken4));

            page.Content()
                .Column(column =>
                {
                    column.Spacing(Mm(_style.HeaderGapMm));
                    column.Item().Element(ComposeHeader);
                    column.Item().Element(ComposeBody);

                    if (!string.IsNullOrWhiteSpace(_payload.Order?.Note))
                    {
                        column.Item()
                            .PaddingTop(Mm(8))
                            .Text(Text(_payload.Order.Note));
                    }
                });
        });
    }

    private void ComposeHeader(IContainer container)
    {
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
            default:
                ComposeItemsTable(container);
                break;
        }
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
            var hasTotals = _payload.TotalRows is { Count: > 0 };

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

            if (_payload.PaymentTotals is { Count: > 0 })
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
        if (_payload.TotalRows is not { Count: > 0 })
            return;

        ComposeLabeledRows(container, _payload.TotalRows);
    }

    private void ComposeLabeledRows(IContainer container, IReadOnlyList<LabeledValueRow> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(Mm(30));
            });

            foreach (var row in rows)
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
}

sealed class LabeledValueRow
{
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("emphasis")]
    public bool Emphasis { get; init; }
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
