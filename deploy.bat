del /f /s /q publish >nul
dotnet publish --output publish/ -r linux-arm -c Debug

ssh pi@raspberry pkill -f RaspberryStreamer
scp publish\* pi@raspberry:/home/pi/raspberry-streamer/
ssh pi@raspberry chmod +x /home/pi/raspberry-streamer/RaspberryStreamer
ssh pi@raspberry /home/pi/raspberry-streamer/RaspberryStreamer
popd