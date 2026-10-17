#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# RUN sed -i 's/TLSv1.2/TLSv1/g' /etc/ssl/openssl.cnf
# RUN sed -i 's/DEFAULT@SECLEVEL=2/DEFAULT@SECLEVEL=1/g' /etc/ssl/openssl.cnf
WORKDIR /app
ENV TZ="America/Lima"

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Domain/Domain.csproj", "Domain/"]
COPY ["CoredbInfrastructure/CoredbInfrastructure.csproj", "CoredbInfrastructure/"]
COPY ["MongoInfrastructure/MongoInfrastructure.csproj", "MongoInfrastructure/"]
COPY ["CognitoInfrastructure/CognitoInfrastructure.csproj", "CognitoInfrastructure/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["ApiUserManagement/ApiUserManagement.csproj", "ApiUserManagement/"]

RUN dotnet restore "ApiUserManagement/ApiUserManagement.csproj"

COPY . .
WORKDIR "/src/ApiUserManagement"
RUN dotnet build "ApiUserManagement.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ApiUserManagement.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
#Certificado para DocumentDB
RUN apt-get update && apt-get install -y wget && \
    wget -O /etc/ssl/certs/global-bundle.pem https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem
ENTRYPOINT ["dotnet", "ApiUserManagement.dll"]

#------- To Deploy ----------------------------------------------
#> docker build -t img-dash-manager.
#> docker run --restart=on-failure -p 5701:5001 \
# -e ASPNETCORE_URLS=http://+:5001 \
# -e MONGO_DB_SERVER=123 \
# -e MONGO_DB_NAME=1234 \
# -e MONGO_DB_USER=234 \
# -e MONGO_DB_PASSWD=12 \
# -e CORE_DB_SERVER=23 \
# -e CORE_DB_NAME=545 \
# -e CORE_DB_USER=4354 \
# -e CORE_DB_PASSWD=343 \
# -e COGNITO_USER_POOL_ID=us-east-1_F2uF8JO3m \
# -e COGNITO_REGION=us-east-1 \
# --name api-dash-mamanger -d img-dash-manager
#> --restart=always
#> --rm
