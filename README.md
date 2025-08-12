High performance ipfix collector for tracking NAT events (NAT translation logging), with predefined ipfix template for CKAT-BRAS (https://vasexperts.ru/products/skat/)

System requirements: Windows/Linux (systemd-based distr) + .NET 8.0+, 16GB+ RAM, 4-8 core cpu (production is under Intel(R) Xeon(R) CPU E5-2680 v2 @ 2.80GHz), high speed large space storage system.


Linux sysctl tunning recommendations:
net.core.netdev_max_backlog=10000
net.core.wmem_max=33554432
net.core.rmem_max=33554432
net.core.rmem_default=8388608
net.core.wmem_default=4194394
net.ipv4.udp_mem=8388608 12582912 16777216
net.ipv4.udp_rmem_min=32768
net.ipv4.udp_wmem_min=32768
net.core.optmem_max=25165824

tools:
- collectorService - ipfix sensor, receives ipfix packets, stores raw data
- fixclean - tool for deduplicating data (if fixpack ran many times on same data)
- fixpack - tool for aggregating start-stop events in compact flow for long-term storage
- searcher - tool for searching events in time range by ip in aggregated data (after fixpack)

space requirements:
calculated by rules: one raw event = 52 byte, one aggregated flow = 55 byte, header size = 20b;
for raw unpacked data, ~6GB per four for storing 60gbps cgnat average load under 30k+ customers, or ~150GB per day
minimum 4TB per month cycle buffer with packing every night (by cron) and storing in long-term storage (estimated calculation 15TB per year then fixpack chained with gzip)

To run as service under Linux:
- build solution
```
dotnet publish -c Release -o /home/collector/
```
- create file /etc/systemd/system/ipfixCollector.service :
```[Unit]
Description=ipfixCollector

[Service]
Type=notify
ExecStart=/home/collector/collectorService

[Install]
WantedBy=multi-user.target
```
- reload systemd and enable service
```
  systemctl daemon-reload
  systemctl enable ipfixCollector
  systemctl start ipfixCollector  
```

To run as windows service:
- use sc.exe
```
sc.exe create ipfixCollector binpath= c:\collector\collectorService.exe type= share start= auto
net start ipfixCollector
```

