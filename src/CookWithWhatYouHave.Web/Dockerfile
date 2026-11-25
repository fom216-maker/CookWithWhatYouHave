FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CookWithWhatYouHave.Contracts/CookWithWhatYouHave.Contracts.csproj CookWithWhatYouHave.Contracts/
COPY src/CookWithWhatYouHave.Web/CookWithWhatYouHave.Web.csproj CookWithWhatYouHave.Web/
RUN dotnet restore CookWithWhatYouHave.Web/CookWithWhatYouHave.Web.csproj
COPY src/CookWithWhatYouHave.Contracts/ CookWithWhatYouHave.Contracts/
COPY src/CookWithWhatYouHave.Web/ CookWithWhatYouHave.Web/
RUN dotnet publish CookWithWhatYouHave.Web/CookWithWhatYouHave.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER $APP_UID
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CookWithWhatYouHave.Web.dll"]
