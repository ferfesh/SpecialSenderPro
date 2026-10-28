@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ========================================
echo   جاري رفع ملفات SpecialSenderPro إلى GitHub
echo ========================================
echo.
git push -u origin main
echo.
if %errorlevel% equ 0 (
    echo [OK] تم الرفع إلى GitHub بنجاح!
) else (
    echo [ERROR] حدث خطأ أثناء الرفع، تأكد من تسجيل دخولك إلى حساب GitHub في المتصفح.
)
echo.
pause

