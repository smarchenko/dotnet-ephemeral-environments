# Unit tests of the image selection of ./deploy/prune-images.ps1. Run: pwsh -NoProfile -c "Invoke-Pester tests/deploy -Output Detailed"

BeforeAll {
    Import-Module "$PSScriptRoot/../../deploy/lib/EphemeralEnvs.Deploy.psm1" -Force
}

Describe 'Select-UnusedEnvImages' {
    It 'selects images whose tag no deployed env uses' {
        $images = 'ee/api:aaaaaaaaaaaa', 'ee/web:aaaaaaaaaaaa', 'ee/api:bbbbbbbbbbbb'
        Select-UnusedEnvImages -Images $images -UsedTags 'bbbbbbbbbbbb' |
            Should -Be @('ee/api:aaaaaaaaaaaa', 'ee/web:aaaaaaaaaaaa')
    }

    It 'keeps the migrator of a deployed env although no container runs it' {
        $images = 'ee/api:bbbbbbbbbbbb', 'ee/web:bbbbbbbbbbbb', 'ee/migrator:bbbbbbbbbbbb'
        @(Select-UnusedEnvImages -Images $images -UsedTags 'bbbbbbbbbbbb').Count | Should -Be 0
    }

    It 'selects a stale worktree tag wt-<env>' {
        Select-UnusedEnvImages -Images 'ee/api:wt-master', 'ee/api:cccccccccccc' -UsedTags 'cccccccccccc' |
            Should -Be 'ee/api:wt-master'
    }

    It 'ignores images outside ee/ and untagged ones' {
        @(Select-UnusedEnvImages -Images 'postgres:17', 'ee/api:<none>', 'other/ee/api:x' -UsedTags @()).Count |
            Should -Be 0
    }

    It 'selects every ee image when no env is deployed' {
        Select-UnusedEnvImages -Images 'ee/api:x', 'ee/migrator:y' -UsedTags @() |
            Should -Be @('ee/api:x', 'ee/migrator:y')
    }
}
