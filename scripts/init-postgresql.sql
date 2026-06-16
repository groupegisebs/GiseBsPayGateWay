-- Script d'initialisation PostgreSQL pour GISEBS Pay Gateway
-- Exécuter en tant que superutilisateur PostgreSQL

CREATE USER gisebs_pay WITH PASSWORD 'ChangeMe_Strong_Password';

CREATE DATABASE gisebs_pay_gateway
    WITH OWNER = gisebs_pay
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8'
    TEMPLATE = template0;

GRANT ALL PRIVILEGES ON DATABASE gisebs_pay_gateway TO gisebs_pay;

\c gisebs_pay_gateway

GRANT ALL ON SCHEMA public TO gisebs_pay;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO gisebs_pay;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO gisebs_pay;

-- Les tables seront créées par Entity Framework Core via:
-- dotnet ef database update
