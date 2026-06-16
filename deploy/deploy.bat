@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-all.ps1" %*
