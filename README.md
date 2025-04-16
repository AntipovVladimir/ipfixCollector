High performance ipfix collector for tracking NAT events (NAT translation logging), with predefined ipfix template for CKAT-BRAS. 

Contains:
- collectorService - main entry point for running collector service via cli or systemd unit or windows service
- ipfixCollector - collector server library, with predefined limits for 256k event packets per second (near upto 64,000 active users on 100G+ bandwidth BRAS)
- fixpack - cli utility for assembling nat event records to paired flows (with begin / end translation timestamps)
- fixclean - cli utility for cleaning duplicate flow records (for cases there fixpack ran many times on same files)
- searcher - cli utility for searching certain nat events at date/time period (within 10 min window) for legal reasons (like police inquiries)

Requires .net 8 
Recommended system requirements for reaching full performance: 
- CPU Intel Xeon E5-2680 v2 or higher (not necessary, utilization about 15-20%)
- 16+ GB RAM
- SSD/NVME 2-4TB for one-month ring buffer (with every week scheduled fixpack processing to long-term storage)
- 10-12TB per year storage, sata raid 5-6 will be enough (long-term storage)
- minimum 1G NIC (10G recommended)
- UDP sockets tunning

sysctl tunning recommendations:
net.core.somaxconn=262144
net.core.wmem_max=33554432
net.core.rmem_max=33554432
net.core.rmem_default=8388608
net.core.wmem_default=4194394
net.ipv4.udp_mem=8388608 12582912 16777216
net.ipv4.udp_rmem_min=32768
net.ipv4.udp_wmem_min=32768
net.core.optmem_max=25165824


windows udp tunning via registry:
[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AFD\Parameters]
"DefaultReceiveWindow"=dword:00080000
"DefaultSendWindow"=dword:00040000
