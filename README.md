# factorymes-modbus-simulator

FactoryMES의 `simulators/modbus-simulator` 배포 단위입니다. 다른 서비스의 소스나 솔루션을 가져오지 않고 빌드합니다.
공통 계약/런타임 코드는 정확한 NuGet 버전을 고정하며, 공개 릴리스 자산을 SHA-256으로 검증합니다.

```powershell
pwsh -NoProfile -File build/restore-packages.ps1
dotnet restore simulators/modbus-simulator/FactoryMES.ModbusSimulator.csproj --locked-mode
dotnet build simulators/modbus-simulator/FactoryMES.ModbusSimulator.csproj -c Release --no-restore

docker build -t factorymes/modbus-simulator:dev .
```

`.packages`는 다운로드 캐시이며 Git에 저장하지 않습니다. 패키지 업그레이드는
`build/package-lock.json`, 중앙 버전, NuGet lock 파일을 명시적으로 갱신하고 검증합니다.
공통 소스 저장소의 변경만으로 이 저장소의 고정된 의존성이나 배포가 바뀌지 않습니다.

CI는 이 서비스와 소유 계약 검사만 빌드합니다. 버전 태그의 GHCR 이미지 발행은 이 이미지에만 적용됩니다.
실제 환경 배포·Compose·여러 서비스의 통합 검증은 `factorymes-system` 저장소가 소유합니다.
독립 서비스 프로세스가 실행될 때 필요한 비밀 파일과 환경 설정은 로컬 운영 구성에서 제공합니다.
암호·인증서·실제 운영 IP·개인 경로는 이 저장소에 저장하지 않습니다.
실제 외부 메일/채팅·유료 AI 호출은 기본 정책에서 비활성입니다.
