-- Créer la base GISEBS Pay Gateway (une fois)
-- Sur le serveur : sudo -u postgres psql -f deploy/scripts/create-database.sql

SELECT 'CREATE DATABASE "gisebs_pay_gateway" OWNER gisedocuser'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'gisebs_pay_gateway')\gexec

GRANT ALL PRIVILEGES ON DATABASE "gisebs_pay_gateway" TO gisedocuser;

\c "gisebs_pay_gateway"

GRANT ALL ON SCHEMA public TO gisedocuser;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO gisedocuser;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO gisedocuser;
