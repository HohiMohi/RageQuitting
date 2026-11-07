[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string[]]$TraceDirectory,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedDirs = @($TraceDirectory | ForEach-Object { [IO.Path]::GetFullPath($_) })
if (-not $OutputPath) {
    if ($resolvedDirs.Count -eq 1) { $OutputPath = Join-Path $resolvedDirs[0] 'analysis-summary.json' }
    else { $OutputPath = Join-Path (Get-Location) 'single-carry-analysis-summary.json' }
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$eventNames = @('MovementUpdateEnd','PreviewRefresh','RequestSubmitted','AuthorityPush','ReceiveCallbackBeforeBase','ReceiveCallbackAfterBase','ReceiveCacheUpdated','ReceiveCacheCleared','ServerValidationRead','PlacementResponse','SessionBegin','SessionFrozen')
$traces = [Collections.Generic.List[object]]::new()
$errors = [Collections.Generic.List[object]]::new()
$seenRunIds = @{}

function Convert-Number($Value) {
    if ($null -eq $Value) { return $null }
    $number = 0.0
    if ([double]::TryParse([string]$Value,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number)) { return $number }
    return $null
}

function Get-Distance($A,$B) {
    if (-not $A -or -not $B) { return $null }
    $dx=[double]$A.x-[double]$B.x; $dy=[double]$A.y-[double]$B.y; $dz=[double]$A.z-[double]$B.z
    return [Math]::Sqrt($dx*$dx+$dy*$dy+$dz*$dz)
}

function Test-PositionState($A,$B,[int]$Mask) {
    foreach ($axis in @(@('x',1),@('y',2),@('z',4))) {
        if (($Mask -band $axis[1]) -and [Math]::Abs([double]$A.($axis[0])-[double]$B.($axis[0])) -gt 0.0001) { return $false }
    }
    return $true
}

function Test-RotationState($A,$B,[int]$Mask,[bool]$Quaternion) {
    if ($Mask -eq 0) { return $true }
    $dot=[Math]::Abs(([double]$A.x*[double]$B.x)+([double]$A.y*[double]$B.y)+([double]$A.z*[double]$B.z)+([double]$A.w*[double]$B.w))
    $normA=[Math]::Sqrt(([double]$A.x*[double]$A.x)+([double]$A.y*[double]$A.y)+([double]$A.z*[double]$A.z)+([double]$A.w*[double]$A.w))
    $normB=[Math]::Sqrt(([double]$B.x*[double]$B.x)+([double]$B.y*[double]$B.y)+([double]$B.z*[double]$B.z)+([double]$B.w*[double]$B.w))
    if ($normA -le 0 -or $normB -le 0) { return $false }
    return ($dot / ($normA * $normB)) -ge 0.999999
}

for ($directoryIndex=0; $directoryIndex -lt $resolvedDirs.Count; $directoryIndex++) {
    $dir=$resolvedDirs[$directoryIndex]
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { $errors.Add([ordered]@{path=$dir;reason='directory-missing'}); continue }
    foreach ($file in Get-ChildItem -LiteralPath $dir -Filter '*.json' -File -Recurse) {
        try { $data=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -ErrorAction Stop }
        catch { $errors.Add([ordered]@{path=$file.FullName;reason='invalid-json';detail=$_.Exception.Message}); continue }
        # Only top-level runId+records identifies a trial. Metadata snapshots and indexes are ignored.
        if (-not $data.PSObject.Properties['runId'] -or -not $data.PSObject.Properties['records']) { continue }
        $runId=[string]$data.runId
        if (-not $runId) { $errors.Add([ordered]@{path=$file.FullName;reason='missing-run-id'}); continue }
        if ($seenRunIds.ContainsKey($runId)) { $errors.Add([ordered]@{path=$file.FullName;reason='duplicate-run-id';runId=$runId;firstPath=$seenRunIds[$runId]}); continue }
        $seenRunIds[$runId]=$file.FullName
        $records=@($data.records)
        $metadata=$null
        try { if ($data.metadataJson) { $metadata=([string]$data.metadataJson | ConvertFrom-Json -ErrorAction Stop) } } catch { }
        $issues=[Collections.Generic.List[string]]::new()
        if (-not $metadata) { $issues.Add('missing-or-malformed-metadata') }
        if ($data.overflow -or @($records | Where-Object { $_.overflow }).Count -gt 0) { $issues.Add('overflow') }
        $eventCounts=[ordered]@{}
        for ($i=0;$i -lt $eventNames.Count;$i++) { $eventCounts[$eventNames[$i]]=@($records | Where-Object { $_.eventKind -eq $i }).Count }
        $sessionBegin=@($records | Where-Object { $_.eventKind -eq 10 })
        $sessionFreeze=@($records | Where-Object { $_.eventKind -eq 11 })
        if (-not $sessionBegin.Count) { $issues.Add('missing-session-begin') }
        if (-not $sessionFreeze.Count) { $issues.Add('missing-session-freeze-or-abort') }
        $initialSync=@($records | Where-Object { $_.isInitialSynchronization })
        $requestRecords=@($records | Where-Object { $_.eventKind -eq 2 -and $_.requestId -gt 0 })
        $requestIds=@($requestRecords | ForEach-Object { [string]$_.requestId } | Sort-Object -Unique)
        $requestSummaries=[Collections.Generic.List[object]]::new()
        foreach ($requestId in $requestIds) {
            $submitted=@($requestRecords | Where-Object { [string]$_.requestId -eq $requestId })
            $request=$null
            if ($submitted.Count -eq 1) { $request=$submitted[0] }
            $sameId=@($records | Where-Object { [string]$_.requestId -eq $requestId })
            $responses=@($sameId | Where-Object { $_.eventKind -eq 9 })
            $validations=@($sameId | Where-Object { $_.eventKind -eq 8 })
            $requestIssues=[Collections.Generic.List[string]]::new()
            if ($submitted.Count -ne 1) { $requestIssues.Add('ambiguous-submit-count') }
            if ($responses.Count -ne 1) { $requestIssues.Add($(if ($responses.Count -eq 0) {'missing-response'} else {'ambiguous-response'})) }
            if ($validations.Count -ne 1) { $requestIssues.Add($(if ($validations.Count -eq 0) {'missing-validation'} else {'ambiguous-validation'})) }
            if ($requestIssues.Count -gt 0) { $issues.Add("request-$requestId-incomplete-or-ambiguous") }
            $validation=if ($validations.Count -eq 1) { $validations[0] } else { $null }
            $response=if ($responses.Count -eq 1) { $responses[0] } else { $null }
            $ownerPushes=@()
            $ownerReplicaRecords=@()
            $ownerRegistration=@()
            $serverRegistration=@()
            $serverReceives=@()
            $cacheEvents=@()
            $handoffMatches=[Collections.Generic.List[object]]::new()
            $pushBefore=$null; $pushAfter=$null; $cacheBefore=$null; $cacheAfter=$null
            if ($request) {
                $ownerReplicaRecords=@($records | Where-Object { $_.peerLocalClientId -eq $request.peerLocalClientId -and
                    $_.managerInstanceId -eq $request.managerInstanceId -and $_.playerInstanceId -eq $request.playerInstanceId -and
                    $_.localSpawnGeneration -eq $request.localSpawnGeneration })
                $ownerRegistration=@($ownerReplicaRecords | Where-Object { $_.eventKind -eq 10 })
                $ownerPushes=@($records | Where-Object {
                    $_.eventKind -eq 3 -and -not $_.isInitialSynchronization -and $_.hasSourceTick -and
                    $_.managerInstanceId -eq $request.managerInstanceId -and $_.playerInstanceId -eq $request.playerInstanceId -and
                    $_.localSpawnGeneration -eq $request.localSpawnGeneration -and $_.peerLocalClientId -eq $request.peerLocalClientId
                } | Sort-Object sequence)
                $pushBefore=@($ownerPushes | Where-Object { $_.sequence -lt $request.sequence } | Select-Object -Last 1)
                $pushAfter=@($ownerPushes | Where-Object { $_.sequence -gt $request.sequence } | Select-Object -First 1)
                if ($pushBefore.Count) { $pushBefore=$pushBefore[0] } else { $pushBefore=$null }
                if ($pushAfter.Count) { $pushAfter=$pushAfter[0] } else { $pushAfter=$null }
            }
            if ($validation) {
                $serverRecords=@($records | Where-Object { $_.peerLocalClientId -eq $validation.peerLocalClientId -and $_.peerIsServer -and $_.managerInstanceId -eq $validation.managerInstanceId -and
                    $_.playerInstanceId -eq $validation.playerInstanceId -and $_.localSpawnGeneration -eq $validation.localSpawnGeneration })
                $serverRegistration=@($serverRecords | Where-Object { $_.eventKind -eq 10 })
                $serverReceives=@($serverRecords | Where-Object { $_.eventKind -in @(4,5) -and $_.hasSourceTick -and -not $_.isInitialSynchronization })
                $cacheEvents=@($serverRecords | Where-Object { $_.eventKind -in @(6,7) } | Sort-Object sequence)
                $cacheBefore=@($cacheEvents | Where-Object { $_.sequence -lt $validation.sequence } | Select-Object -Last 1)
                $cacheAfter=@($cacheEvents | Where-Object { $_.sequence -gt $validation.sequence } | Select-Object -First 1)
                if ($cacheBefore.Count) {$cacheBefore=$cacheBefore[0]} else {$cacheBefore=$null}
                if ($cacheAfter.Count) {$cacheAfter=$cacheAfter[0]} else {$cacheAfter=$null}
            }
            if ($request -and $validation) {
                $receiveBoundaries=@($serverReceives | Where-Object { $_.eventKind -eq 4 })
                $unobservedOwnerHistory=$false
                foreach ($receive in $receiveBoundaries) {
                    $contextAvailable = $receive.PSObject.Properties['inLocalSpace'] -and $receive.PSObject.Properties['playerObjectId'] -and $receive.PSObject.Properties['ownerClientId'] -and $receive.PSObject.Properties['quaternionSync']
                    if (-not $contextAvailable -or $receive.inLocalSpace) {
                        $handoffMatches.Add([ordered]@{status='state-handoff-unverified-context';serverCallbackSequence=$receive.sequence;sourceTick=$receive.sourceTick;missingOrUnsupportedContext=if(-not $contextAvailable){'identity-space-or-sync-field-missing'}else{'local-space-parent-context-not-recorded'};initialSynchronization=$false})
                        $issues.Add("request-$requestId-state-handoff-unverified-context")
                        continue
                    }
                    $sameStateOtherIdentity = @($ownerPushes | Where-Object {
                        $_.sequence -lt $validation.sequence -and $_.sourceTick -eq $receive.sourceTick -and
                        $_.PSObject.Properties['inLocalSpace'] -and -not $_.inLocalSpace -and
                        $_.PSObject.Properties['playerObjectId'] -and $_.PSObject.Properties['ownerClientId'] -and $_.PSObject.Properties['quaternionSync'] -and
                        $_.positionChangeMask -eq $receive.positionChangeMask -and $_.rotationChangeMask -eq $receive.rotationChangeMask -and
                        (Test-PositionState $_.statePosition $receive.statePosition ([int]$receive.positionChangeMask)) -and
                        (Test-RotationState $_.stateRotation $receive.stateRotation ([int]$receive.rotationChangeMask) ([bool]$receive.quaternionSync)) -and
                        ($_.playerObjectId -ne $receive.playerObjectId -or $_.ownerClientId -ne $receive.ownerClientId -or $_.quaternionSync -ne $receive.quaternionSync)
                    }).Count
                    $candidates=@($ownerPushes | Where-Object {
                        $_.sequence -lt $validation.sequence -and $_.sourceTick -eq $receive.sourceTick -and
                        $_.PSObject.Properties['inLocalSpace'] -and -not $_.inLocalSpace -and
                        $_.PSObject.Properties['playerObjectId'] -and $_.PSObject.Properties['ownerClientId'] -and $_.PSObject.Properties['quaternionSync'] -and
                        $_.playerObjectId -eq $receive.playerObjectId -and $_.ownerClientId -eq $receive.ownerClientId -and $_.quaternionSync -eq $receive.quaternionSync -and
                        $_.positionChangeMask -eq $receive.positionChangeMask -and $_.rotationChangeMask -eq $receive.rotationChangeMask -and
                        (Test-PositionState $_.statePosition $receive.statePosition ([int]$receive.positionChangeMask)) -and
                        (Test-RotationState $_.stateRotation $receive.stateRotation ([int]$receive.rotationChangeMask) ([bool]$receive.quaternionSync))
                    })
                    if ($candidates.Count -eq 1) {
                        $push=$candidates[0]
                        $registrationOK=($ownerRegistration.Count -eq 1 -and $serverRegistration.Count -eq 1 -and $receive.managerInstanceId -eq $validation.managerInstanceId -and $receive.playerInstanceId -eq $validation.playerInstanceId -and $receive.localSpawnGeneration -eq $validation.localSpawnGeneration)
                        $handoffMatches.Add([ordered]@{status=if($registrationOK){'unique-observed-state-match'}else{'state-match-registration-unverified'};ownerPushSequence=$push.sequence;serverCallbackSequence=$receive.sequence;sourceTick=$receive.sourceTick;positionMask=$receive.positionChangeMask;rotationMask=$receive.rotationChangeMask;quaternionSync=$receive.quaternionSync;rotationMatchMethod='normalized-sign-invariant-full-quaternion';initialSynchronization=$false;ownerPeerRegistrationCount=$ownerRegistration.Count;serverPeerRegistrationCount=$serverRegistration.Count;registrationsMatchOwnEvents=$registrationOK;sameNetworkObjectAndOwner=$true;sameQuaternionSyncMode=$true;excludedSameStateForeignIdentityOrSyncCandidates=$sameStateOtherIdentity;worldSpaceOnly=$true;peerLocalGenerationsKeptSeparate=$true})
                        if (-not $registrationOK) { $issues.Add("request-$requestId-state-match-registration-unverified") }
                    } elseif ($candidates.Count -gt 1) {
                        $handoffMatches.Add([ordered]@{status='ambiguous-repeated-state';candidatePushSequences=@($candidates | ForEach-Object { $_.sequence });serverCallbackSequence=$receive.sequence;sourceTick=$receive.sourceTick;initialSynchronization=$false})
                    } elseif ($ownerPushes.Count -eq 0) {
                        $handoffMatches.Add([ordered]@{status='state-handoff-unobserved-owner-history';serverCallbackSequence=$receive.sequence;sourceTick=$receive.sourceTick;historyBoundary='no owner authority push was recorded in this trace window';initialSynchronization=$false})
                        $unobservedOwnerHistory=$true
                    }
                }
                if (@($handoffMatches | Where-Object { $_.status -eq 'ambiguous-repeated-state' }).Count -gt 0) { $issues.Add("request-$requestId-ambiguous-state-handoff") }
                if ($serverReceives.Count -gt 0 -and $handoffMatches.Count -eq 0 -and -not $unobservedOwnerHistory) { $issues.Add("request-$requestId-no-matched-observed-state-handoff") }
            }
            $responseMilliseconds=$null
            if ($request -and $response -and $request.peerLocalClientId -eq $response.peerLocalClientId) {
                $responseMilliseconds=([double]$response.realtime-[double]$request.realtime)*1000.0
            }
            $requestSummaries.Add([ordered]@{
                requestId=$requestId;submitCount=$submitted.Count;responseCount=$responses.Count;validationCount=$validations.Count
                responseResult=if($response){$response.result}else{$null};validationBranch=if($validation){$validation.branch}else{$null}
                measuredResponseMilliseconds=Convert-Number $data.measuredResponseMilliseconds
                requestToResponseSamePeerMilliseconds=$responseMilliseconds
                residualMeters=if($validation -and $validation.hasResidual){[Math]::Sqrt([double]$validation.residualSquared)}else{$null}
                authoritativePoseRead=if($validation){[bool]$validation.hasAuthoritativePoseRead}else{$null}
                completeCache=if($validation){[bool]$validation.hasCompleteCache}else{$null}
                cacheEventBeforeValidation=if($cacheBefore){[ordered]@{kind=$cacheBefore.eventKind;sequence=$cacheBefore.sequence;sourceTick=$cacheBefore.sourceTick;position=$cacheBefore.cachedPositionAfter}}else{$null}
                cacheEventAfterValidation=if($cacheAfter){[ordered]@{kind=$cacheAfter.eventKind;sequence=$cacheAfter.sequence;sourceTick=$cacheAfter.sourceTick;position=$cacheAfter.cachedPositionAfter}}else{$null}
                lastOwnerPushBeforeRequest=if($pushBefore){[ordered]@{sequence=$pushBefore.sequence;frame=$pushBefore.frame;sourceTick=$pushBefore.sourceTick;position=$pushBefore.statePosition;positionMask=$pushBefore.positionChangeMask;rotationMask=$pushBefore.rotationChangeMask}}else{$null}
                firstOwnerPushAfterRequest=if($pushAfter){[ordered]@{sequence=$pushAfter.sequence;frame=$pushAfter.frame;sourceTick=$pushAfter.sourceTick;position=$pushAfter.statePosition}}else{$null}
                validationQuery=if($validation){[ordered]@{sequence=$validation.sequence;frame=$validation.frame;sourceTick=$validation.sourceTick;localTick=$validation.localTick;serverTick=$validation.serverTick;position=if($validation.hasAuthoritativePoseRead){$validation.bodyPosition}else{$null};renderedBodyPositionFallback=if(-not $validation.hasAuthoritativePoseRead){$validation.bodyPosition}else{$null};hasPoseRead=$validation.hasAuthoritativePoseRead;completeCache=$validation.hasCompleteCache;inLocalSpace=$validation.inLocalSpace}}else{$null}
                lastPushToValidationPoseDistanceMeters=if($pushBefore -and $validation -and $validation.hasAuthoritativePoseRead -and -not $pushBefore.inLocalSpace){Get-Distance $pushBefore.statePosition $validation.bodyPosition}else{$null}
                lastPushToValidationPoseDistanceUnavailableReason=if($validation -and -not $validation.hasAuthoritativePoseRead){'authoritative pose read failed; bodyPosition is the rendered fallback'}elseif($pushBefore -and $pushBefore.inLocalSpace){'push state is local-space; validation body pose is world-space and parent context is not recorded'}else{$null}
                matchedStateHandoffs=@($handoffMatches);issues=@($requestIssues)
                tickDomains=[ordered]@{
                    source=@($sameId | Where-Object { $_.hasSourceTick } | ForEach-Object { [ordered]@{peerLocalClientId=$_.peerLocalClientId;sourceTick=$_.sourceTick;eventKind=$_.eventKind;sequence=$_.sequence} })
                    ownerLocal=@($ownerReplicaRecords | Where-Object { $_.hasLocalTick } | ForEach-Object { [ordered]@{peerLocalClientId=$_.peerLocalClientId;peerIsServer=$_.peerIsServer;localTick=$_.localTick;eventKind=$_.eventKind;sequence=$_.sequence} })
                    server=@($sameId | Where-Object { $_.hasServerTick -and $_.peerIsServer } | ForEach-Object { [ordered]@{peerLocalClientId=$_.peerLocalClientId;serverTick=$_.serverTick;eventKind=$_.eventKind;sequence=$_.sequence} })
                }
            })
        }
        if ($requestIds.Count -eq 0) { $issues.Add('no-placement-request-observed') }
        if ($eventCounts.SessionBegin -gt 0 -and $eventCounts.SessionFrozen -eq 0) { $issues.Add('capture-aborted-before-freeze') }
        $traces.Add([ordered]@{
            path=[IO.Path]::GetRelativePath($dir,$file.FullName);inputDirectoryIndex=$directoryIndex;runId=$runId;recordCount=$records.Count;capacityPerPeer=$data.capacityPerPeer;overflow=[bool]$data.overflow
            caseName=if($metadata){$metadata.caseName}else{$null};packetDelayMs=if($metadata){$metadata.packetDelayMs}else{$null}
            simulatorSettings=if($metadata){[ordered]@{preset=$metadata.simulatorPresetName;connected=$metadata.simulatorConnected;jitterMs=$metadata.jitterMs;lossPercent=$metadata.lossPercent;lossInterval=$metadata.lossInterval;ownerUseUnreliableDeltas=$metadata.ownerUseUnreliableDeltas;serverUseUnreliableDeltas=$metadata.serverUseUnreliableDeltas;ownerSyncAxes=$metadata.ownerSyncAxes;serverSyncAxes=$metadata.serverSyncAxes;ownerPositionThreshold=$metadata.ownerPositionThreshold;serverPositionThreshold=$metadata.serverPositionThreshold;ownerRotationThreshold=$metadata.ownerRotationThreshold;serverRotationThreshold=$metadata.serverRotationThreshold;ownerQuaternionSync=$metadata.ownerQuaternionSync;serverQuaternionSync=$metadata.serverQuaternionSync;ownerInLocalSpace=$metadata.ownerInLocalSpace;serverInLocalSpace=$metadata.serverInLocalSpace}}else{$null}
            measuredResponseMilliseconds=Convert-Number $data.measuredResponseMilliseconds
            initialSynchronization=[ordered]@{recordCount=$initialSync.Count;ownerPushes=@($initialSync | Where-Object { $_.eventKind -eq 3 }).Count;receiveCallbackBoundaryRecords=@($initialSync | Where-Object { $_.eventKind -in @(4,5) }).Count;physicalObservedCallbackCount=@($initialSync | Where-Object { $_.eventKind -eq 4 }).Count;pairedBeforeBoundaryRecords=@($initialSync | Where-Object { $_.eventKind -eq 4 }).Count;pairedAfterBoundaryRecords=@($initialSync | Where-Object { $_.eventKind -eq 5 }).Count}
            eventCounts=$eventCounts;requests=@($requestSummaries);issues=@($issues)
            limitations=@('packetDelayMs comes from trace metadata as the transport simulator delay; measuredResponseMilliseconds is a separate observed duration.','Tick clocks are kept in separate source, owner-local, and server domains; values are never subtracted across domains.','Quaternion values are compared sign-invariantly because raw transmitted Euler components were not captured; a match is reconstructed state evidence, not wire-component proof.','State handoff matching requires matching object/owner identity and quaternion-sync mode. Local-space matches are left unverified because parent transform context is not recorded.','Owner pushes may be matched through validation, including pushes observed after request submission; last pre-request push remains a separate sample.','A receive with no owner push in the recorded trace window is marked as unobserved owner history, not proof that no earlier push existed.','A matching observer hook is evidence of matching observed state, not proof of a physical transport send or NGO queue delivery.','Owner and server spawn generations are peer-local and are reported/validated per replica, never compared across peers.','Missing data and ambiguous state matches remain explicitly unresolved.','Embedded metadata/snapshots are not additional trials.')
        })
    }
}
$summary=[ordered]@{schemaVersion=1;createdAtUtc=[DateTime]::UtcNow.ToString('o');inputDirectories=$resolvedDirs;traceCount=$traces.Count;incompleteOrAmbiguousCount=@($traces | Where-Object { $_.issues.Count -gt 0 }).Count;traces=@($traces);inputErrors=@($errors)}
$parent=Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$summary | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$summary | ConvertTo-Json -Depth 30
if ($traces.Count -eq 0 -or $errors.Count -gt 0 -or @($traces | Where-Object { $_.issues.Count -gt 0 -or $_.overflow }).Count -gt 0) { exit 1 }
exit 0
