@echo off
del /f /s /q publish >nul
dotnet publish --output publish/ -r linux-arm -c Release

ssh pi@raspberry sudo systemctl stop raspberry-streamer
scp publish\* pi@raspberry:/home/pi/raspberry-streamer/
ssh pi@raspberry chmod +x /home/pi/raspberry-streamer/RaspberryStreamer
ssh pi@raspberry sudo systemctl daemon-reload
ssh pi@raspberry sudo systemctl start raspberry-streamer
popd
