@echo off
@set /p angle=Input Angle:
echo Rotate %angle%
cd In
for %%a in (*.*) do (
	echo "%%a"
    ..\NConvert-win64\XnView\nconvert.exe -o "..\Out\%%a_r180.jpg"  -rotate %angle% "%%a"
)
pause