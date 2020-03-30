@echo off
del /f /s /q RaspberryStreamer\bin\Debug\netcoreapp3.1\linux-arm >nul
dotnet publish -r linux-arm /p:ShowLinkerSizeComparison=true /p:PublishTrimmed=true
REM /p:PublishSingleFile=true /p:PublishTrimmed=true

pushd RaspberryStreamer\bin\Debug\netcoreapp3.1\linux-arm\publish
ssh pi@raspberry pkill -f RaspberryStreamer
scp * pi@raspberry:/home/pi/raspberry-streamer/
ssh pi@raspberry chmod +x /home/pi/raspberry-streamer/RaspberryStreamer
ssh pi@raspberry /home/pi/raspberry-streamer/RaspberryStreamer
popd