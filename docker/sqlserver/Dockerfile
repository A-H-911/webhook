FROM mcr.microsoft.com/mssql/server:2022-latest

COPY init.sql /init.sql
COPY entrypoint.sh /entrypoint.sh

USER root
RUN chmod +x /entrypoint.sh

USER mssql
ENTRYPOINT ["/entrypoint.sh"]
