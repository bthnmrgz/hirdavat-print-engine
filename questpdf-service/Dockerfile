FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY questpdf-service/HirdavatQuestPdf.Api.csproj questpdf-service/
RUN dotnet restore questpdf-service/HirdavatQuestPdf.Api.csproj

COPY questpdf-service/ questpdf-service/
RUN dotnet publish questpdf-service/HirdavatQuestPdf.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5159
EXPOSE 5159

COPY --from=build /app/publish ./
RUN mkdir -p /app/generated-pdfs

ENTRYPOINT ["dotnet", "HirdavatQuestPdf.Api.dll"]
